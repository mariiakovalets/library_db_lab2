using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace LibraryNoSql.Bench;

/// <summary>Catalog in PostgreSQL: 9 normalized tables (see sql/schema.sql).</summary>
public sealed class SqlCatalogStore(string connectionString) : ICatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(connectionString);

    // authors of one book, used by every query that shows a list of books
    private const string AuthorsOfBook =
        "ARRAY(SELECT a.full_name FROM book_authors ba JOIN authors a ON a.id = ba.author_id WHERE ba.book_id = b.id)";

    public string Name => "PostgreSQL";

    public async Task ResetAsync()
    {
        await using var cmd = _dataSource.CreateCommand(
            "TRUNCATE book_copies, audiobook_details, ebook_details, paper_book_details, book_authors, books, authors, genres, publishers");
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Bulk load with COPY, the fastest way to fill PostgreSQL tables.</summary>
    public async Task LoadAsync(Dataset data)
    {
        await using (var conn = await _dataSource.OpenConnectionAsync())
        {
            await CopyAsync(conn, "COPY publishers (id, name) FROM STDIN (FORMAT BINARY)", data.Publishers, async (w, p) =>
            {
                await w.WriteAsync(p.Id, NpgsqlDbType.Integer);
                await w.WriteAsync(p.Name, NpgsqlDbType.Varchar);
            });

            await CopyAsync(conn, "COPY genres (id, name) FROM STDIN (FORMAT BINARY)", data.Genres, async (w, g) =>
            {
                await w.WriteAsync(g.Id, NpgsqlDbType.Integer);
                await w.WriteAsync(g.Name, NpgsqlDbType.Varchar);
            });

            await CopyAsync(conn, "COPY authors (id, full_name, birth_year) FROM STDIN (FORMAT BINARY)", data.Authors, async (w, a) =>
            {
                await w.WriteAsync(a.Id, NpgsqlDbType.Integer);
                await w.WriteAsync(a.FullName, NpgsqlDbType.Varchar);
                await w.WriteAsync(a.BirthYear, NpgsqlDbType.Integer);
            });

            await CopyAsync(conn,
                "COPY books (id, isbn, title, book_type, publisher_id, genre_id, publish_year) FROM STDIN (FORMAT BINARY)",
                data.Books, async (w, b) =>
                {
                    await w.WriteAsync(b.Id, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.Isbn, NpgsqlDbType.Varchar);
                    await w.WriteAsync(b.Title, NpgsqlDbType.Varchar);
                    await w.WriteAsync(b.Type, NpgsqlDbType.Varchar);
                    await w.WriteAsync(b.PublisherId, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.GenreId, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.PublishYear, NpgsqlDbType.Integer);
                });

            await CopyAsync(conn, "COPY book_authors (book_id, author_id) FROM STDIN (FORMAT BINARY)",
                data.Books.SelectMany(b => b.AuthorIds.Select(a => (BookId: b.Id, AuthorId: a))), async (w, x) =>
                {
                    await w.WriteAsync(x.BookId, NpgsqlDbType.Integer);
                    await w.WriteAsync(x.AuthorId, NpgsqlDbType.Integer);
                });

            await CopyAsync(conn, "COPY paper_book_details (book_id, pages_count, cover_type) FROM STDIN (FORMAT BINARY)",
                data.Books.Where(b => b.Paper is not null), async (w, b) =>
                {
                    await w.WriteAsync(b.Id, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.Paper!.PagesCount, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.Paper.CoverType, NpgsqlDbType.Varchar);
                });

            await CopyAsync(conn, "COPY ebook_details (book_id, file_format, file_size_mb) FROM STDIN (FORMAT BINARY)",
                data.Books.Where(b => b.Ebook is not null), async (w, b) =>
                {
                    await w.WriteAsync(b.Id, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.Ebook!.FileFormat, NpgsqlDbType.Varchar);
                    await w.WriteAsync(b.Ebook.FileSizeMb, NpgsqlDbType.Numeric);
                });

            await CopyAsync(conn, "COPY audiobook_details (book_id, duration_minutes, narrator) FROM STDIN (FORMAT BINARY)",
                data.Books.Where(b => b.Audio is not null), async (w, b) =>
                {
                    await w.WriteAsync(b.Id, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.Audio!.DurationMinutes, NpgsqlDbType.Integer);
                    await w.WriteAsync(b.Audio.Narrator, NpgsqlDbType.Varchar);
                });

            await CopyAsync(conn, "COPY book_copies (id, book_id, inventory_number, status) FROM STDIN (FORMAT BINARY)",
                data.Books.SelectMany(b => b.Copies.Select(c => (BookId: b.Id, Copy: c))), async (w, x) =>
                {
                    await w.WriteAsync(x.Copy.Id, NpgsqlDbType.Integer);
                    await w.WriteAsync(x.BookId, NpgsqlDbType.Integer);
                    await w.WriteAsync(x.Copy.InventoryNumber, NpgsqlDbType.Varchar);
                    await w.WriteAsync(x.Copy.Status, NpgsqlDbType.Varchar);
                });
        }

        // fresh statistics for the planner and a visibility map for index-only scans
        await using var vacuum = _dataSource.CreateCommand("VACUUM ANALYZE");
        await vacuum.ExecuteNonQueryAsync();
    }

    public async Task<long> CountBooksAsync()
    {
        await using var cmd = _dataSource.CreateCommand("SELECT count(*) FROM books");
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<long> StorageBytesAsync()
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT sum(pg_total_relation_size(relid))::bigint FROM pg_stat_user_tables");
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<BookCard?> GetBookCardAsync(int bookId)
    {
        // one round trip: the book row, its 1:1 details, publisher, genre, authors and copies
        await using var cmd = _dataSource.CreateCommand($"""
            SELECT b.id, b.isbn, b.title, b.book_type, b.publish_year, p.name, g.name,
                   pd.pages_count, pd.cover_type, ed.file_format, ed.file_size_mb, ad.duration_minutes, ad.narrator,
                   {AuthorsOfBook},
                   COALESCE((SELECT json_agg(json_build_object('inventoryNumber', c.inventory_number, 'status', c.status) ORDER BY c.id)
                             FROM book_copies c WHERE c.book_id = b.id), '[]')
            FROM books b
            JOIN publishers p ON p.id = b.publisher_id
            JOIN genres g ON g.id = b.genre_id
            LEFT JOIN paper_book_details pd ON pd.book_id = b.id
            LEFT JOIN ebook_details ed ON ed.book_id = b.id
            LEFT JOIN audiobook_details ad ON ad.book_id = b.id
            WHERE b.id = @id
            """);
        cmd.Parameters.AddWithValue("id", bookId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var type = reader.GetString(3);
        return new BookCard(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            type,
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            Details.SortAuthors(reader.GetFieldValue<string[]>(13)),
            Details.Format(type,
                NullableInt(reader, 7), NullableString(reader, 8),
                NullableString(reader, 9), reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                NullableInt(reader, 11), NullableString(reader, 12)),
            JsonSerializer.Deserialize<CopyInfo[]>(reader.GetString(14), JsonOptions)!);
    }

    public Task<List<CatalogItem>> GetCatalogPageAsync(int genreId, int fromYear, int toYear) =>
        QueryCatalogItemsAsync($"""
            SELECT b.id, b.title, b.publish_year, p.name, {AuthorsOfBook}
            FROM books b
            JOIN publishers p ON p.id = b.publisher_id
            WHERE b.genre_id = @genre AND b.publish_year BETWEEN @from AND @to
            ORDER BY b.publish_year DESC, b.id DESC
            LIMIT 20
            """,
            ("genre", genreId), ("from", fromYear), ("to", toYear));

    public Task<List<CatalogItem>> GetBooksByAuthorAsync(int authorId) =>
        QueryCatalogItemsAsync($"""
            SELECT b.id, b.title, b.publish_year, p.name, {AuthorsOfBook}
            FROM book_authors x
            JOIN books b ON b.id = x.book_id
            JOIN publishers p ON p.id = b.publisher_id
            WHERE x.author_id = @author
            ORDER BY b.publish_year DESC, b.id DESC
            """,
            ("author", authorId));

    public async Task<List<GenreCount>> CountBooksByGenreAsync()
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT g.name, count(*) FROM books b JOIN genres g ON g.id = b.genre_id GROUP BY g.name");

        var result = new List<GenreCount>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new GenreCount(reader.GetString(0), reader.GetInt64(1)));
        }

        return result.OrderBy(x => x.Genre, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A new book touches up to 4 tables. All inserts go in one transaction and one round trip (a batch).
    /// </summary>
    public async Task AddBookAsync(Book book, Dataset data)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var batch = new NpgsqlBatch(conn, tx);

        batch.BatchCommands.Add(Command(
            "INSERT INTO books (id, isbn, title, book_type, publisher_id, genre_id, publish_year) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            book.Id, book.Isbn, book.Title, book.Type, book.PublisherId, book.GenreId, book.PublishYear));

        batch.BatchCommands.Add(Command(
            "INSERT INTO book_authors (book_id, author_id) SELECT $1, unnest($2::int[])",
            book.Id, book.AuthorIds));

        if (book.Paper is not null)
        {
            batch.BatchCommands.Add(Command(
                "INSERT INTO paper_book_details (book_id, pages_count, cover_type) VALUES ($1, $2, $3)",
                book.Id, book.Paper.PagesCount, book.Paper.CoverType));
        }
        else if (book.Ebook is not null)
        {
            batch.BatchCommands.Add(Command(
                "INSERT INTO ebook_details (book_id, file_format, file_size_mb) VALUES ($1, $2, $3)",
                book.Id, book.Ebook.FileFormat, book.Ebook.FileSizeMb));
        }
        else if (book.Audio is not null)
        {
            batch.BatchCommands.Add(Command(
                "INSERT INTO audiobook_details (book_id, duration_minutes, narrator) VALUES ($1, $2, $3)",
                book.Id, book.Audio.DurationMinutes, book.Audio.Narrator));
        }

        if (book.Copies.Count > 0)
        {
            batch.BatchCommands.Add(Command(
                "INSERT INTO book_copies (id, book_id, inventory_number, status) SELECT c.id, $2, c.inv, c.st FROM unnest($1::int[], $3::text[], $4::text[]) AS c(id, inv, st)",
                book.Copies.Select(c => c.Id).ToArray(), book.Id,
                book.Copies.Select(c => c.InventoryNumber).ToArray(), book.Copies.Select(c => c.Status).ToArray()));
        }

        await batch.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    /// <summary>The author's name lives in one row, so a rename changes one row.</summary>
    public async Task RenameAuthorAsync(int authorId, string newName)
    {
        await using var cmd = _dataSource.CreateCommand("UPDATE authors SET full_name = @name WHERE id = @id");
        cmd.Parameters.AddWithValue("name", newName);
        cmd.Parameters.AddWithValue("id", authorId);
        await cmd.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private async Task<List<CatalogItem>> QueryCatalogItemsAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = _dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var result = new List<CatalogItem>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new CatalogItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                Details.SortAuthors(reader.GetFieldValue<string[]>(4))));
        }

        return result;
    }

    private static async Task CopyAsync<T>(
        NpgsqlConnection conn, string copySql, IEnumerable<T> rows, Func<NpgsqlBinaryImporter, T, Task> writeRow)
    {
        await using var writer = await conn.BeginBinaryImportAsync(copySql);
        foreach (var row in rows)
        {
            await writer.StartRowAsync();
            await writeRow(writer, row);
        }

        await writer.CompleteAsync();
    }

    private static NpgsqlBatchCommand Command(string sql, params object[] values)
    {
        var command = new NpgsqlBatchCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return command;
    }

    private static int? NullableInt(NpgsqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
    private static string? NullableString(NpgsqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
}

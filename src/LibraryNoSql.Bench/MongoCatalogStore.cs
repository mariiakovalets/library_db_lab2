using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace LibraryNoSql.Bench;

// One document of the "books" collection. It holds what PostgreSQL keeps in 9 tables:
// publisher, genre and authors are embedded, type-specific fields sit in paper / ebook / audio,
// and only the field of the book's own type is present (null fields are not stored).
public class BookDocument
{
    [BsonId] public int Id { get; set; }
    public string Isbn { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public int PublishYear { get; set; }
    public RefDocument Publisher { get; set; } = new();
    public RefDocument Genre { get; set; } = new();
    public List<AuthorRef> Authors { get; set; } = [];
    public PaperDocument? Paper { get; set; }
    public EbookDocument? Ebook { get; set; }
    public AudioDocument? Audio { get; set; }
    public List<CopyDocument>? Copies { get; set; }
}

public class RefDocument
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AuthorRef
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
}

public class PaperDocument
{
    public int PagesCount { get; set; }
    public string CoverType { get; set; } = "";
}

public class EbookDocument
{
    public string FileFormat { get; set; } = "";
    [BsonRepresentation(BsonType.Decimal128)] public decimal FileSizeMb { get; set; }
}

public class AudioDocument
{
    public int DurationMinutes { get; set; }
    public string Narrator { get; set; } = "";
}

public class CopyDocument
{
    public string InventoryNumber { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>Catalog in MongoDB: one "books" collection, one document per book.</summary>
public sealed class MongoCatalogStore : ICatalogStore
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BookDocument> _books;

    // the fields a catalog list shows; the rest of the document is not sent over the network
    private static readonly ProjectionDefinition<BookDocument> ListProjection = Builders<BookDocument>.Projection
        .Include(b => b.Title)
        .Include(b => b.PublishYear)
        .Include("publisher.name")
        .Include("authors.fullName");

    private static readonly SortDefinition<BookDocument> NewestFirst = Builders<BookDocument>.Sort
        .Descending(b => b.PublishYear)
        .Descending(b => b.Id);

    static MongoCatalogStore()
    {
        ConventionRegistry.Register(
            "library",
            new ConventionPack { new CamelCaseElementNameConvention(), new IgnoreIfNullConvention(true) },
            _ => true);
    }

    public MongoCatalogStore(string connectionString)
    {
        _database = new MongoClient(connectionString).GetDatabase("library");
        // wait until the change is in the journal on disk, as PostgreSQL waits for its WAL on commit
        _books = _database.GetCollection<BookDocument>("books")
            .WithWriteConcern(WriteConcern.W1.With(journal: true));
    }

    public string Name => "MongoDB";

    public Task ResetAsync() => _database.DropCollectionAsync("books");

    public async Task LoadAsync(Dataset data)
    {
        // the same indexes as in sql/schema.sql; _id is indexed automatically
        await _books.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BookDocument>(Builders<BookDocument>.IndexKeys
                .Ascending("genre._id").Descending(b => b.PublishYear).Descending(b => b.Id)),
            new CreateIndexModel<BookDocument>(Builders<BookDocument>.IndexKeys.Ascending("authors._id"))
        ]);

        foreach (var chunk in data.Books.Chunk(5000))
        {
            await _books.InsertManyAsync(chunk.Select(b => ToDocument(b, data)), new InsertManyOptions { IsOrdered = false });
        }
    }

    public Task<long> CountBooksAsync() => _books.CountDocumentsAsync(FilterDefinition<BookDocument>.Empty);

    public async Task<long> StorageBytesAsync()
    {
        // MongoDB writes data files at checkpoints (once a minute); force one so the size is not measured too early
        await _database.Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("fsync", 1));

        var stats = await _books.Aggregate()
            .AppendStage<BsonDocument>(new BsonDocument("$collStats", new BsonDocument("storageStats", new BsonDocument())))
            .FirstAsync();
        var storage = stats["storageStats"].AsBsonDocument;
        return storage["storageSize"].ToInt64() + storage["totalIndexSize"].ToInt64();
    }

    public async Task<BookCard?> GetBookCardAsync(int bookId)
    {
        // the whole page is one document
        var doc = await _books.Find(b => b.Id == bookId).FirstOrDefaultAsync();
        if (doc is null)
        {
            return null;
        }

        return new BookCard(
            doc.Id,
            doc.Isbn,
            doc.Title,
            doc.Type,
            doc.PublishYear,
            doc.Publisher.Name,
            doc.Genre.Name,
            Details.SortAuthors(doc.Authors.Select(a => a.FullName)),
            Details.Format(doc.Type,
                doc.Paper?.PagesCount, doc.Paper?.CoverType,
                doc.Ebook?.FileFormat, doc.Ebook?.FileSizeMb,
                doc.Audio?.DurationMinutes, doc.Audio?.Narrator),
            (doc.Copies ?? []).Select(c => new CopyInfo(c.InventoryNumber, c.Status)).ToArray());
    }

    public Task<List<CatalogItem>> GetCatalogPageAsync(int genreId, int fromYear, int toYear)
    {
        var filter = Builders<BookDocument>.Filter.Eq("genre._id", genreId)
                     & Builders<BookDocument>.Filter.Gte(b => b.PublishYear, fromYear)
                     & Builders<BookDocument>.Filter.Lte(b => b.PublishYear, toYear);

        return FindCatalogItemsAsync(filter, limit: 20);
    }

    public Task<List<CatalogItem>> GetBooksByAuthorAsync(int authorId) =>
        FindCatalogItemsAsync(Builders<BookDocument>.Filter.Eq("authors._id", authorId), limit: null);

    public async Task<List<GenreCount>> CountBooksByGenreAsync()
    {
        var groups = await _books.Aggregate()
            .Group(new BsonDocument { { "_id", "$genre.name" }, { "count", new BsonDocument("$sum", 1) } })
            .ToListAsync();

        return groups
            .Select(g => new GenreCount(g["_id"].AsString, g["count"].ToInt64()))
            .OrderBy(x => x.Genre, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A new book is one insert of one document.</summary>
    public Task AddBookAsync(Book book, Dataset data) => _books.InsertOneAsync(ToDocument(book, data));

    /// <summary>
    /// The author's name is copied into every book of the author, so a rename has to update all of them.
    /// This is the price of keeping the whole book in one document.
    /// </summary>
    public Task RenameAuthorAsync(int authorId, string newName) =>
        _books.UpdateManyAsync(
            Builders<BookDocument>.Filter.Eq("authors._id", authorId),
            Builders<BookDocument>.Update.Set("authors.$[a].fullName", newName),
            new UpdateOptions
            {
                ArrayFilters = [new BsonDocumentArrayFilterDefinition<BsonDocument>(new BsonDocument("a._id", authorId))]
            });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<List<CatalogItem>> FindCatalogItemsAsync(FilterDefinition<BookDocument> filter, int? limit)
    {
        var docs = await _books.Find(filter)
            .Sort(NewestFirst)
            .Limit(limit)
            .Project<BookDocument>(ListProjection)
            .ToListAsync();

        return docs
            .Select(d => new CatalogItem(d.Id, d.Title, d.PublishYear, d.Publisher.Name,
                Details.SortAuthors(d.Authors.Select(a => a.FullName))))
            .ToList();
    }

    private static BookDocument ToDocument(Book book, Dataset data)
    {
        var publisher = data.PublisherOf(book);
        var genre = data.GenreOf(book);

        return new BookDocument
        {
            Id = book.Id,
            Isbn = book.Isbn,
            Title = book.Title,
            Type = book.Type,
            PublishYear = book.PublishYear,
            Publisher = new RefDocument { Id = publisher.Id, Name = publisher.Name },
            Genre = new RefDocument { Id = genre.Id, Name = genre.Name },
            Authors = book.AuthorIds
                .Select(id => new AuthorRef { Id = id, FullName = data.AuthorById(id).FullName })
                .ToList(),
            Paper = book.Paper is null ? null : new PaperDocument { PagesCount = book.Paper.PagesCount, CoverType = book.Paper.CoverType },
            Ebook = book.Ebook is null ? null : new EbookDocument { FileFormat = book.Ebook.FileFormat, FileSizeMb = book.Ebook.FileSizeMb },
            Audio = book.Audio is null ? null : new AudioDocument { DurationMinutes = book.Audio.DurationMinutes, Narrator = book.Audio.Narrator },
            Copies = book.Copies.Count == 0
                ? null
                : book.Copies.Select(c => new CopyDocument { InventoryNumber = c.InventoryNumber, Status = c.Status }).ToList()
        };
    }
}

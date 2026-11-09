namespace LibraryNoSql.Bench;

/// <summary>
/// The same set of catalog operations, implemented once for PostgreSQL and once for MongoDB.
/// The benchmark calls both implementations with the same inputs.
/// </summary>
public interface ICatalogStore : IAsyncDisposable
{
    string Name { get; }

    Task ResetAsync();
    Task LoadAsync(Dataset data);
    Task<long> CountBooksAsync();
    /// <summary>Size of data and indexes on disk, in bytes.</summary>
    Task<long> StorageBytesAsync();

    /// <summary>Everything a reader sees on the book page.</summary>
    Task<BookCard?> GetBookCardAsync(int bookId);
    /// <summary>20 newest books of a genre published in the given years.</summary>
    Task<List<CatalogItem>> GetCatalogPageAsync(int genreId, int fromYear, int toYear);
    Task<List<CatalogItem>> GetBooksByAuthorAsync(int authorId);
    Task<List<GenreCount>> CountBooksByGenreAsync();

    Task AddBookAsync(Book book, Dataset data);
    Task RenameAuthorAsync(int authorId, string newName);
}

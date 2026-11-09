using System.Globalization;

namespace LibraryNoSql.Bench;

// Generated data: the same objects go into both databases.

public record Publisher(int Id, string Name);
public record Genre(int Id, string Name);
public record Author(int Id, string FullName, int BirthYear);
public record Copy(int Id, string InventoryNumber, string Status);

public record PaperDetails(int PagesCount, string CoverType);
public record EbookDetails(string FileFormat, decimal FileSizeMb);
public record AudioDetails(int DurationMinutes, string Narrator);

public class Book
{
    public required int Id { get; init; }
    public required string Isbn { get; init; }
    public required string Title { get; init; }
    /// <summary>paper, ebook or audio</summary>
    public required string Type { get; init; }
    public required int PublisherId { get; init; }
    public required int GenreId { get; init; }
    public required int PublishYear { get; init; }
    public required int[] AuthorIds { get; init; }
    public PaperDetails? Paper { get; init; }
    public EbookDetails? Ebook { get; init; }
    public AudioDetails? Audio { get; init; }
    /// <summary>Physical copies, only for paper books.</summary>
    public List<Copy> Copies { get; init; } = [];
}

public class Dataset
{
    public required List<Publisher> Publishers { get; init; }
    public required List<Genre> Genres { get; init; }
    public required List<Author> Authors { get; init; }
    public required List<Book> Books { get; init; }

    // ids are 1..N without gaps, so a list index is enough for a lookup
    public Publisher PublisherOf(Book book) => Publishers[book.PublisherId - 1];
    public Genre GenreOf(Book book) => Genres[book.GenreId - 1];
    public Author AuthorById(int id) => Authors[id - 1];

    public int CopyCount => Books.Sum(b => b.Copies.Count);
}

// Query results: both stores return these types, so their answers can be compared.

public record CopyInfo(string InventoryNumber, string Status);

public record BookCard(
    int Id,
    string Isbn,
    string Title,
    string Type,
    int PublishYear,
    string Publisher,
    string Genre,
    string[] Authors,
    string Details,
    CopyInfo[] Copies);

public record CatalogItem(int Id, string Title, int PublishYear, string Publisher, string[] Authors);

public record GenreCount(string Genre, long Count);

public static class Details
{
    /// <summary>One text line with the fields that only this kind of book has.</summary>
    public static string Format(
        string type,
        int? pagesCount, string? coverType,
        string? fileFormat, decimal? fileSizeMb,
        int? durationMinutes, string? narrator) => type switch
    {
        "paper" => $"{pagesCount} pages, {coverType} cover",
        "ebook" => string.Create(CultureInfo.InvariantCulture, $"{fileFormat}, {fileSizeMb:0.00} MB"),
        "audio" => $"{durationMinutes} min, narrator {narrator}",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string[] SortAuthors(IEnumerable<string> names) =>
        names.Order(StringComparer.Ordinal).ToArray();
}

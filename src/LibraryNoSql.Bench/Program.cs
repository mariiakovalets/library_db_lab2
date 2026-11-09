using System.Diagnostics;
using System.Globalization;
using System.Text;
using LibraryNoSql.Bench;

// Usage (from the repository root, with docker compose up):
//   dotnet run -c Release --project src/LibraryNoSql.Bench -- 100000
// The argument is the number of books. Results go to results/results-<books>.md.

const string PostgresConnection =
    "Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=library;Max Auto Prepare=20;Auto Prepare Min Usages=2";
const string MongoConnection = "mongodb://localhost:27017";

var bookCount = args.Length > 0 ? int.Parse(args[0]) : 100_000;
// dot as the decimal separator, space between thousands: 1 000 000
var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
culture.NumberFormat.NumberGroupSeparator = " ";
CultureInfo.CurrentCulture = culture;

await using ICatalogStore sql = new SqlCatalogStore(PostgresConnection);
await using ICatalogStore mongo = new MongoCatalogStore(MongoConnection);

Console.WriteLine($"Generating {bookCount:N0} books...");
var data = DataGenerator.Generate(bookCount);
var bookAuthorRows = data.Books.Sum(b => b.AuthorIds.Length);

// 1. Load the same data into both databases
var loadSeconds = new Dictionary<string, double>();
foreach (var store in new[] { sql, mongo })
{
    Console.Write($"Loading into {store.Name}... ");
    await store.ResetAsync();
    var start = Stopwatch.GetTimestamp();
    await store.LoadAsync(data);
    loadSeconds[store.Name] = Stopwatch.GetElapsedTime(start).TotalSeconds;
    Console.WriteLine($"{loadSeconds[store.Name]:0.0} s, {await store.CountBooksAsync():N0} books");
}

var storageBytes = new Dictionary<string, long>
{
    [sql.Name] = await sql.StorageBytesAsync(),
    [mongo.Name] = await mongo.StorageBytesAsync()
};

// 2. Both databases must give the same answers before we compare their speed
Console.WriteLine("Checking that both databases return the same results:");
var checkRandom = new Random(1);
var checks = new List<(string What, int Checked, int Mismatched)>();

var cardIds = Enumerable.Range(0, 200).Select(_ => checkRandom.Next(1, bookCount + 1)).ToArray();
checks.Add(await ResultCheck.CompareAsync("Картка книги", cardIds.Length,
    async (i, s) => await s.GetBookCardAsync(cardIds[i]), sql, mongo));

var pageInputs = Enumerable.Range(0, 100).Select(_ => RandomPage(checkRandom)).ToArray();
checks.Add(await ResultCheck.CompareAsync("Сторінка каталогу", pageInputs.Length,
    async (i, s) => await s.GetCatalogPageAsync(pageInputs[i].Genre, pageInputs[i].From, pageInputs[i].To), sql, mongo));

var checkAuthors = Enumerable.Range(0, 100).Select(_ => checkRandom.Next(1, data.Authors.Count + 1)).ToArray();
checks.Add(await ResultCheck.CompareAsync("Книги автора", checkAuthors.Length,
    async (i, s) => await s.GetBooksByAuthorAsync(checkAuthors[i]), sql, mongo));

checks.Add(await ResultCheck.CompareAsync("Кількість книг за жанрами", 1,
    async (_, s) => await s.CountBooksByGenreAsync(), sql, mongo));

// 3. Timed scenarios. Every scenario uses one fixed list of inputs for both databases.
Console.WriteLine("Measuring:");
const int warmup = 50, reads = 500;
var random = new Random(2026);
var results = new List<ScenarioResult>();

var bookIds = Enumerable.Range(0, warmup + reads).Select(_ => random.Next(1, bookCount + 1)).ToArray();
results.Add(await Benchmark.RunAsync(
    "Картка книги за id",
    "найчастіша дія: читач відкриває сторінку книги з усіма даними",
    sql, mongo, warmup, reads,
    (s, i) => s.GetBookCardAsync(bookIds[i])));

var pages = Enumerable.Range(0, warmup + reads).Select(_ => RandomPage(random)).ToArray();
results.Add(await Benchmark.RunAsync(
    "Сторінка каталогу (жанр + роки, 20 найновіших)",
    "типовий перегляд каталогу з фільтром і сортуванням",
    sql, mongo, warmup, reads,
    (s, i) => s.GetCatalogPageAsync(pages[i].Genre, pages[i].From, pages[i].To)));

var authorIds = Enumerable.Range(0, warmup + reads).Select(_ => random.Next(1, data.Authors.Count + 1)).ToArray();
results.Add(await Benchmark.RunAsync(
    "Усі книги автора",
    "пошук за автором: у SQL через таблицю-зв'язку, у MongoDB за полем усередині масиву",
    sql, mongo, warmup, reads,
    (s, i) => s.GetBooksByAuthorAsync(authorIds[i])));

results.Add(await Benchmark.RunAsync(
    "Кількість книг за жанрами",
    "звіт по всьому каталогу: агрегація без фільтра, читає всі книги",
    sql, mongo, warmup: 5, iterations: 50,
    (s, _) => s.CountBooksByGenreAsync()));

// writes come last: they change the data the read scenarios use
const int writes = 300, writeWarmup = 20;
var newBooks = DataGenerator.GenerateMore(data, writeWarmup + writes, seed: 7);
results.Add(await Benchmark.RunAsync(
    "Додавання однієї книги",
    "поповнення фонду: у SQL до 4 таблиць в одній транзакції, у MongoDB один документ",
    sql, mongo, writeWarmup, writes,
    (s, i) => s.AddBookAsync(newBooks[i], data)));

var renamed = Enumerable.Range(0, writeWarmup + writes).Select(_ => random.Next(1, data.Authors.Count + 1)).ToArray();
var newNames = DataGenerator.GenerateNames(writeWarmup + writes, seed: 11);
results.Add(await Benchmark.RunAsync(
    "Перейменування автора",
    "рідка зміна довідника: у SQL один рядок, у MongoDB ім'я скопійоване в кожну книгу автора",
    sql, mongo, writeWarmup, writes,
    (s, i) => s.RenameAuthorAsync(renamed[i], newNames[i])));

// 4. Report
var report = BuildReport();
var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
Directory.CreateDirectory(resultsDir);
var reportPath = Path.Combine(resultsDir, $"results-{bookCount}.md");
await File.WriteAllTextAsync(reportPath, report);
Console.WriteLine($"Report: {reportPath}");

string BuildReport()
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Результати замірів: {bookCount:N0} книг");
    sb.AppendLine();
    sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm}. Машина: {Environment.ProcessorCount} логічних ядер, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");
    sb.AppendLine("PostgreSQL 18 і MongoDB 8.0 у Docker, кожна з лімітом 4 CPU і 2 ГБ пам'яті, кеш даних 512 МБ.");
    sb.AppendLine();
    sb.AppendLine("## Дані");
    sb.AppendLine();
    sb.AppendLine("| Що | Кількість |");
    sb.AppendLine("|---|---:|");
    sb.AppendLine($"| Книги (документи MongoDB / рядки `books`) | {data.Books.Count:N0} |");
    sb.AppendLine($"| Автори | {data.Authors.Count:N0} |");
    sb.AppendLine($"| Видавництва | {data.Publishers.Count:N0} |");
    sb.AppendLine($"| Жанри | {data.Genres.Count:N0} |");
    sb.AppendLine($"| Рядки `book_authors` | {bookAuthorRows:N0} |");
    sb.AppendLine($"| Примірники (`book_copies`) | {data.CopyCount:N0} |");
    sb.AppendLine();
    sb.AppendLine("## Завантаження й розмір");
    sb.AppendLine();
    sb.AppendLine("| База | Завантаження всього набору | Розмір даних з індексами |");
    sb.AppendLine("|---|---:|---:|");
    foreach (var name in new[] { sql.Name, mongo.Name })
    {
        sb.AppendLine($"| {name} | {loadSeconds[name]:0.0} с | {storageBytes[name] / 1024.0 / 1024.0:0.0} МБ |");
    }

    sb.AppendLine();
    sb.AppendLine("## Перевірка, що бази повертають однакові результати");
    sb.AppendLine();
    sb.AppendLine("| Запит | Перевірено | Збіглося |");
    sb.AppendLine("|---|---:|---:|");
    foreach (var (what, checkedCount, mismatched) in checks)
    {
        sb.AppendLine($"| {what} | {checkedCount} | {checkedCount - mismatched} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Час виконання, мс");
    sb.AppendLine();
    sb.AppendLine("Медіана — типовий час одного запиту, p95 — 95% запитів не довші за це значення.");
    sb.AppendLine();
    sb.AppendLine("| Сценарій | Навіщо | Повторів | PostgreSQL медіана | PostgreSQL p95 | MongoDB медіана | MongoDB p95 | Швидше |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
    foreach (var r in results)
    {
        var ratio = Math.Max(r.Sql.MedianMs, r.Mongo.MedianMs) / Math.Min(r.Sql.MedianMs, r.Mongo.MedianMs);
        var faster = ratio < 1.1
            ? "приблизно однаково (різниця < 10%)"
            : r.Sql.MedianMs < r.Mongo.MedianMs
                ? $"PostgreSQL у {ratio:0.0} раза"
                : $"MongoDB у {ratio:0.0} раза";
        sb.AppendLine($"| {r.Scenario} | {r.Why} | {r.Sql.Iterations} | {r.Sql.MedianMs:0.000} | {r.Sql.P95Ms:0.000} | {r.Mongo.MedianMs:0.000} | {r.Mongo.P95Ms:0.000} | {faster} |");
    }

    return sb.ToString();
}

static (int Genre, int From, int To) RandomPage(Random random)
{
    var from = random.Next(1950, 2016);
    return (random.Next(1, 21), from, from + 10);
}

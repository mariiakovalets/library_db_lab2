namespace LibraryNoSql.Bench;

/// <summary>
/// Builds a random but repeatable catalog: the same seed gives the same data on every run.
/// The data is made up, but it looks like a real catalog: real Ukrainian publishers,
/// titles built from genre-specific templates, publication years after the author turned 18.
/// Shares: 60% paper books, 25% e-books, 15% audiobooks; one author per 5 books;
/// 1–3 authors per book; 1–5 copies per paper book.
/// </summary>
public static class DataGenerator
{
    private static readonly string[] PublisherNames =
    [
        "А-ба-ба-га-ла-ма-га", "Видавництво Старого Лева", "Наш Формат", "Фоліо", "Віхола", "Лабораторія",
        "Vivat", "Клуб Сімейного Дозвілля", "Ранок", "Книголав", "Темпора", "Laurus", "Комора",
        "Meridian Czernowitz", "Видавництво Анетти Антоненко", "Pabulum", "Жорж", "ArtHuss", "Discursus",
        "Дух і Літера", "Смолоскип", "Нора-Друк", "Апріорі", "Навчальна книга – Богдан", "Ще одну сторінку",
        "BookChef", "Основи", "Пінзель", "Yakaboo Publishing", "Час майстрів"
    ];

    private static readonly string[] FirstNames =
    [
        "Олена", "Марія", "Оксана", "Ірина", "Софія", "Наталія", "Катерина", "Ольга", "Анна", "Леся",
        "Галина", "Тетяна", "Юлія", "Вікторія", "Христина", "Ярослава", "Дарина", "Соломія", "Людмила", "Зоряна",
        "Іван", "Андрій", "Тарас", "Дмитро", "Петро", "Богдан", "Сергій", "Юрій", "Василь", "Максим",
        "Олександр", "Михайло", "Остап", "Роман", "Володимир", "Ярослав", "Назар", "Степан", "Григорій", "Артем"
    ];

    // surnames that have the same form for women and men
    private static readonly string[] LastNames =
    [
        "Коваль", "Шевчук", "Бондар", "Ткаченко", "Мельник", "Кравець", "Литвин", "Мороз", "Савчук", "Гнатюк",
        "Поліщук", "Лисенко", "Марченко", "Руденко", "Кузьменко", "Олійник", "Павленко", "Гончар", "Зінченко", "Остапчук",
        "Ковальчук", "Бойко", "Кравчук", "Пономаренко", "Ткачук", "Сидоренко", "Мартинюк", "Дяченко", "Романюк", "Левченко",
        "Вовк", "Гуменюк", "Тимошенко", "Карпенко", "Петрук", "Яковенко", "Дорошенко", "Костюк", "Федоренко", "Захарченко",
        "Лук'яненко", "Андрусишин", "Гордієнко", "Білоус", "Скрипник", "Чорновол", "Симоненко", "Приходько", "Нестеренко", "Довгань"
    ];

    // phrases already in the right grammatical case, so titles never mix up endings
    private static readonly string[] Nominative =
    [
        "Тиха ріка", "Останнє літо", "Чорний ліс", "Біла ніч", "Зимовий сад", "Далека країна", "Нічний потяг",
        "Стара бібліотека", "Порожній дім", "Загублений лист", "Зелений острів", "Кам'яний міст", "Північне сяйво",
        "Холодне море", "Довга дорога", "Срібна зоря", "Осіння мелодія", "Синій птах", "Глибока вода", "Червона калина"
    ];

    private static readonly string[] Genitive =
    [
        "старого маяка", "забутого міста", "останнього літа", "чорного лісу", "тихої ріки", "зимового саду",
        "далекої країни", "нічного потяга", "старої бібліотеки", "порожнього дому", "загубленого листа",
        "зеленого острова", "мертвого озера", "кам'яного мосту", "білої ночі", "північного вітру", "срібної зорі"
    ];

    private static readonly string[] NamesGenitive =
    [
        "Марти", "Остапа", "Соломії", "Назара", "Ярини", "Богдана", "Олесі", "Тараса", "Мирослави", "Данила", "Злати", "Матвія"
    ];

    private static readonly string[] Places =
    [
        "у Львові", "в Карпатах", "в Одесі", "у Києві", "у Чернівцях", "у Полтаві", "у степу", "на Поділлі",
        "над Дніпром", "на Хортиці", "в Ужгороді", "у Харкові"
    ];

    private static readonly string[] Regions =
    [
        "Галичини", "Поділля", "Слобожанщини", "Запорозької Січі", "Київської Русі", "Буковини", "Волині",
        "козацької доби", "Гетьманщини", "Карпат", "Закарпаття", "Чернігівщини"
    ];

    private static readonly string[] FamousPeople =
    [
        "Лесі Українки", "Івана Франка", "Тараса Шевченка", "Григорія Сковороди", "Богдана Хмельницького",
        "Михайла Грушевського", "Соломії Крушельницької", "Олександра Довженка", "Марії Приймаченко", "Василя Стуса"
    ];

    private static readonly string[] Decades = ["1960-х", "1970-х", "1980-х", "1990-х", "2000-х", "2010-х"];

    private static readonly (string Nom, string Gen)[] ScienceTopics =
    [
        ("Мозок", "мозку"), ("Всесвіт", "Всесвіту"), ("Гени", "генів"), ("Клімат", "клімату"),
        ("Штучний інтелект", "штучного інтелекту"), ("Океан", "океану"), ("Час", "часу"), ("Віруси", "вірусів"),
        ("Квантова фізика", "квантової фізики"), ("Еволюція", "еволюції")
    ];

    private static readonly (string Nom, string Gen)[] PsychologyTopics =
    [
        ("Тривога", "тривоги"), ("Звички", "звичок"), ("Емоції", "емоцій"), ("Самотність", "самотності"),
        ("Мотивація", "мотивації"), ("Стрес", "стресу"), ("Пам'ять", "пам'яті"), ("Рішення", "рішень")
    ];

    private static readonly string[] PhilosophyTopics =
        ["свободи", "свідомості", "вибору", "істини", "сенсу", "часу", "добра і зла", "мови"];

    private static readonly string[] BusinessTopics =
        ["лідерства", "переговорів", "стартапу", "маркетингу", "інвестицій", "продуктивності", "командної роботи", "продажів"];

    private static readonly string[] SchoolSubjects =
    [
        "математики", "фізики", "хімії", "біології", "історії України", "української мови",
        "англійської мови", "інформатики", "географії", "економіки"
    ];

    private static readonly string[] EssayTopics = ["свободу", "війну", "мову", "пам'ять", "місто", "незалежність", "культуру", "дім"];

    private static readonly string[] SciFiTitles = ["Останній сигнал", "Місто під куполом", "Друга Земля", "Зоряний маршрут", "Сім хвилин до світанку"];

    // genre -> title templates; {nom}, {gen} and the rest are filled from the lists above
    private static readonly (string Genre, string[] Templates)[] Genres =
    [
        ("Проза", ["{nom}", "Повернення {name}", "Тінь {gen}", "Ніч {place}", "Дім {gen}"]),
        ("Поезія", ["{nom}", "Листи до {name}", "Пісні {gen}"]),
        ("Драма", ["{nom}", "Ніч {place}", "Дім {gen}"]),
        ("Детектив", ["Таємниця {gen}", "Справа {gen}", "Вбивство {place}", "Слід {gen}", "Свідок {place}"]),
        ("Фантастика", ["Хроніки {gen}", "{scifi}", "Код {gen}"]),
        ("Фентезі", ["Хроніки {gen}", "Пророцтво {gen}", "Брама {gen}", "Спадок {gen}"]),
        ("Історія", ["Історія {region}", "Таємниці {region}", "Повсякденне життя {region}", "Нариси з історії {region}"]),
        ("Біографія", ["Життя {person}", "Шлях {person}", "Листи {person}"]),
        ("Науково-популярна", ["{sci}: коротка історія", "Таємниці {sciGen}", "Світ {sciGen}"]),
        ("Психологія", ["Психологія {psyGen}", "Анатомія {psyGen}", "{psy}: практичний посібник"]),
        ("Філософія", ["Філософія {phil}", "Нариси про природу {phil}", "Проблема {phil}"]),
        ("Бізнес", ["Мистецтво {biz}", "Основи {biz}", "Секрети {biz}"]),
        ("Дитяча", ["Пригоди {name}", "Казки {gen}"]),
        ("Підліткова", ["Щоденник {name}", "Останнє літо {place}", "Таємниця {gen}"]),
        ("Пригоди", ["Скарб {gen}", "Пригоди {place}", "Експедиція {place}"]),
        ("Горор", ["Шепіт {gen}", "Тінь {gen}", "Ніч {place}"]),
        ("Романтика", ["{nom}", "Літо {place}", "Кохання {place}", "Лист для {name}"]),
        ("Мемуари", ["Щоденник {decade}", "Спогади {decade}", "Роки {place}"]),
        ("Публіцистика", ["Есеї про {about}", "Тексти про {about}", "Розмови про {about}"]),
        ("Навчальна", ["Підручник з {subj}", "Основи {subj}", "Збірник задач з {subj}", "Довідник з {subj}"])
    ];

    private static readonly string[] CopyStatuses = ["available", "available", "available", "loaned", "written_off"];

    public static Dataset Generate(int bookCount, int seed = 42)
    {
        var random = new Random(seed);

        var genres = Genres.Select((g, i) => new Genre(i + 1, g.Genre)).ToList();
        var publishers = PublisherNames.Select((name, i) => new Publisher(i + 1, name)).ToList();

        var authorCount = Math.Max(100, bookCount / 5);
        var authors = Enumerable.Range(1, authorCount)
            .Select(id => new Author(id, FullName(random), random.Next(1900, 2001)))
            .ToList();

        var data = new Dataset { Genres = genres, Publishers = publishers, Authors = authors, Books = [] };

        var nextCopyId = 1;
        for (var id = 1; id <= bookCount; id++)
        {
            data.Books.Add(CreateBook(random, id, ref nextCopyId, data));
        }

        return data;
    }

    /// <summary>New books for the insert test. Ids and copy ids continue after the existing ones.</summary>
    public static List<Book> GenerateMore(Dataset data, int count, int seed)
    {
        var random = new Random(seed);
        var nextCopyId = data.CopyCount + 1;
        var books = new List<Book>(count);

        for (var id = data.Books.Count + 1; books.Count < count; id++)
        {
            books.Add(CreateBook(random, id, ref nextCopyId, data));
        }

        return books;
    }

    /// <summary>Plausible author names for the rename test (for example, a corrected spelling).</summary>
    public static string[] GenerateNames(int count, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => FullName(random)).ToArray();
    }

    private static Book CreateBook(Random random, int id, ref int nextCopyId, Dataset data)
    {
        var roll = random.NextDouble();
        var type = roll < 0.60 ? "paper" : roll < 0.85 ? "ebook" : "audio";

        var authorsPerBook = random.NextDouble() switch
        {
            < 0.75 => 1,
            < 0.95 => 2,
            _ => 3
        };
        var authorIds = new HashSet<int>();
        while (authorIds.Count < authorsPerBook)
        {
            authorIds.Add(random.Next(1, data.Authors.Count + 1));
        }

        // the book comes out after its youngest author turns 18; e-books and audiobooks are recent formats
        var youngestBirthYear = authorIds.Max(a => data.AuthorById(a).BirthYear);
        var firstYear = Math.Max(1950, youngestBirthYear + 18);
        firstYear = type switch
        {
            "ebook" => Math.Max(firstYear, 2005),
            "audio" => Math.Max(firstYear, 2000),
            _ => firstYear
        };

        var genreIndex = random.Next(Genres.Length);

        var copies = new List<Copy>();
        if (type == "paper")
        {
            var copyCount = random.Next(1, 6);
            for (var i = 0; i < copyCount; i++)
            {
                var copyId = nextCopyId++;
                copies.Add(new Copy(copyId, $"INV-{copyId:D8}", Pick(random, CopyStatuses)));
            }
        }

        return new Book
        {
            Id = id,
            Isbn = $"978-{id:D9}",
            Title = Title(random, Genres[genreIndex].Templates),
            Type = type,
            PublisherId = random.Next(1, data.Publishers.Count + 1),
            GenreId = genreIndex + 1,
            PublishYear = random.Next(firstYear, 2026),
            AuthorIds = authorIds.ToArray(),
            Paper = type == "paper"
                ? new PaperDetails(random.Next(80, 901), random.NextDouble() < 0.4 ? "hard" : "soft")
                : null,
            Ebook = type == "ebook"
                ? new EbookDetails(Pick(random, ["epub", "pdf", "mobi"]), Math.Round((decimal)(0.5 + random.NextDouble() * 49.5), 2))
                : null,
            Audio = type == "audio"
                ? new AudioDetails(random.Next(120, 1501), FullName(random))
                : null,
            Copies = copies
        };
    }

    private static string Title(Random random, string[] templates)
    {
        var title = Pick(random, templates);
        var science = Pick(random, ScienceTopics);
        var psychology = Pick(random, PsychologyTopics);

        return title
            .Replace("{nom}", Pick(random, Nominative))
            .Replace("{gen}", Pick(random, Genitive))
            .Replace("{name}", Pick(random, NamesGenitive))
            .Replace("{place}", Pick(random, Places))
            .Replace("{region}", Pick(random, Regions))
            .Replace("{person}", Pick(random, FamousPeople))
            .Replace("{decade}", Pick(random, Decades))
            .Replace("{sciGen}", science.Gen)
            .Replace("{sci}", science.Nom)
            .Replace("{psyGen}", psychology.Gen)
            .Replace("{psy}", psychology.Nom)
            .Replace("{phil}", Pick(random, PhilosophyTopics))
            .Replace("{biz}", Pick(random, BusinessTopics))
            .Replace("{subj}", Pick(random, SchoolSubjects))
            .Replace("{about}", Pick(random, EssayTopics))
            .Replace("{scifi}", Pick(random, SciFiTitles));
    }

    private static string FullName(Random random) => $"{Pick(random, FirstNames)} {Pick(random, LastNames)}";

    private static T Pick<T>(Random random, T[] items) => items[random.Next(items.Length)];
}

-- Book catalog in PostgreSQL: the relational version of the MongoDB "books" collection.
-- One MongoDB document is spread over 9 tables here.
-- Ids are plain INTEGER keys: the data generator assigns them, so both databases hold the same ids.

CREATE TABLE publishers (
    id   INTEGER PRIMARY KEY,
    name VARCHAR(255) NOT NULL UNIQUE
);

CREATE TABLE genres (
    id   INTEGER PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE authors (
    id         INTEGER PRIMARY KEY,
    full_name  VARCHAR(255) NOT NULL,
    birth_year INTEGER
);

CREATE TABLE books (
    id           INTEGER PRIMARY KEY,
    isbn         VARCHAR(17)  NOT NULL UNIQUE,
    title        VARCHAR(255) NOT NULL,
    book_type    VARCHAR(10)  NOT NULL CHECK (book_type IN ('paper', 'ebook', 'audio')),
    publisher_id INTEGER      NOT NULL REFERENCES publishers (id),
    genre_id     INTEGER      NOT NULL REFERENCES genres (id),
    publish_year INTEGER      NOT NULL
);

CREATE TABLE book_authors (
    book_id   INTEGER NOT NULL REFERENCES books (id),
    author_id INTEGER NOT NULL REFERENCES authors (id),
    PRIMARY KEY (book_id, author_id)
);

-- Fields that only one kind of book has. Each table is 1:1 with books.

CREATE TABLE paper_book_details (
    book_id     INTEGER PRIMARY KEY REFERENCES books (id),
    pages_count INTEGER    NOT NULL CHECK (pages_count > 0),
    cover_type  VARCHAR(4) NOT NULL CHECK (cover_type IN ('hard', 'soft'))
);

CREATE TABLE ebook_details (
    book_id      INTEGER PRIMARY KEY REFERENCES books (id),
    file_format  VARCHAR(4)    NOT NULL CHECK (file_format IN ('epub', 'pdf', 'mobi')),
    file_size_mb NUMERIC(6, 2) NOT NULL CHECK (file_size_mb > 0)
);

CREATE TABLE audiobook_details (
    book_id          INTEGER PRIMARY KEY REFERENCES books (id),
    duration_minutes INTEGER      NOT NULL CHECK (duration_minutes > 0),
    narrator         VARCHAR(255) NOT NULL
);

-- Physical copies exist only for paper books.
CREATE TABLE book_copies (
    id               INTEGER PRIMARY KEY,
    book_id          INTEGER     NOT NULL REFERENCES books (id),
    inventory_number VARCHAR(20) NOT NULL UNIQUE,
    status           VARCHAR(20) NOT NULL CHECK (status IN ('available', 'loaned', 'written_off'))
);

-- Indexes for the benchmark queries. MongoDB gets the same set (see MongoCatalogStore.cs).

-- catalog page: genre + year range, newest first
CREATE INDEX ix_books_genre_year ON books (genre_id, publish_year DESC, id DESC);
-- books of an author
CREATE INDEX ix_book_authors_author ON book_authors (author_id);
-- joins from a book to its copies and publisher; MongoDB stores these inside the document
CREATE INDEX ix_book_copies_book ON book_copies (book_id);
CREATE INDEX ix_books_publisher ON books (publisher_id);

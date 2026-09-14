using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Models;

namespace LivreMente.Importer;

public class GutendexBook
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<GutendexAuthor> Authors { get; set; } = [];
    public List<string> Summaries { get; set; } = [];
    public List<string> Bookshelves { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public bool Copyright { get; set; }
    public Dictionary<string, string> Formats { get; set; } = [];

    [JsonPropertyName("download_count")]
    public int DownloadCount { get; set; }
}

public class GutendexAuthor
{
    public string Name { get; set; } = "";

    [JsonPropertyName("birth_year")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("death_year")]
    public int? DeathYear { get; set; }
}

public class GutendexPage
{
    public string? Next { get; set; }
    public List<GutendexBook> Results { get; set; } = [];
}

public class GutendexImporter(HttpClient http, LivreMenteDbContext db)
{
    private readonly HttpClient _http = http;
    private readonly LivreMenteDbContext _db = db;

    public async Task ImportAsync(int? maxPages = null, int startPage = 1)
    {
        string? nextUrl = startPage > 1
            ? $"https://gutendex.com/books/?sort=popular&page={startPage}"
            : "https://gutendex.com/books/?sort=popular";
        int page = 0;

        while (nextUrl is not null && (maxPages is null || page < maxPages))
        {
            var currentPageNumber = startPage + page;
            Console.WriteLine($"Gutendex: página {currentPageNumber}...");

            GutendexPage? result = null;
            int attempt = 0;
            while (result is null)
            {
                try
                {
                    result = await _http.GetFromJsonAsync<GutendexPage>(nextUrl);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    attempt++;
                    if (attempt > 5)
                    {
                        Console.WriteLine($"Falhou {attempt} vezes na página {currentPageNumber}. Abortando.");
                        throw;
                    }
                    var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    Console.WriteLine($"Erro ({ex.GetType().Name}: {ex.Message}). Tentando de novo em {wait.TotalSeconds}s...");
                    await Task.Delay(wait);
                }
            }

            if (result is null) break;

            foreach (var book in result.Results)
            {
                if (book.Copyright) continue; // só domínio público confirmado

                var externalId = book.Id.ToString();
                var exists = await _db.Materials
                    .AnyAsync(m => m.Source == "gutendex" && m.ExternalId == externalId);
                if (exists) continue;

                var material = new Material
                {
                    ExternalId = externalId,
                    Source = "gutendex",
                    Type = "book",
                    Title = book.Title,
                    Language = book.Languages.FirstOrDefault(),
                    Summary = book.Summaries.FirstOrDefault(),
                    CoverUrl = book.Formats.GetValueOrDefault("image/jpeg"),
                    EpubFileUrl = book.Formats.GetValueOrDefault("application/epub+zip"),
                    DownloadCount = book.DownloadCount,
                    CopyrightFlag = book.Copyright
                };

                foreach (var a in book.Authors)
                {
                    var author = _db.Authors.Local.FirstOrDefault(x => x.Name == a.Name)
                        ?? await _db.Authors.FirstOrDefaultAsync(x => x.Name == a.Name);
                    if (author is null)
                    {
                        author = new Author { Name = a.Name, BirthYear = a.BirthYear, DeathYear = a.DeathYear };
                        _db.Authors.Add(author);
                    }
                    material.Authors.Add(author);
                }

                foreach (var shelf in book.Bookshelves)
                {
                    var genre = _db.Genres.Local.FirstOrDefault(x => x.Name == shelf)
                        ?? await _db.Genres.FirstOrDefaultAsync(x => x.Name == shelf);
                    if (genre is null)
                    {
                        genre = new Genre { Name = shelf };
                        _db.Genres.Add(genre);
                    }
                    material.Genres.Add(genre);
                }

                _db.Materials.Add(material);
            }

            await _db.SaveChangesAsync();
                if (page % 50 == 0)
                {
                    _db.ChangeTracker.Clear();
                }

            nextUrl = result.Next;
            page++;
        }
    }
}
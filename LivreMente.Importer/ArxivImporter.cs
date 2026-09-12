using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Models;

namespace LivreMente.Importer;

public class ArxivImporter(HttpClient http, LivreMenteDbContext db)
{
    private readonly HttpClient _http = http;
    private readonly LivreMenteDbContext _db = db;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public async Task ImportAsync(string searchQuery, int totalResults = 300, int pageSize = 100)
    {
        for (int start = 0; start < totalResults; start += pageSize)
        {
            Console.WriteLine($"arXiv ({searchQuery}): itens {start} a {start + pageSize}...");
            var url = $"https://export.arxiv.org/api/query?search_query={searchQuery}" +
                      $"&start={start}&max_results={pageSize}";

            var xml = await _http.GetStringAsync(url);
            var feed = XDocument.Parse(xml);
            var entries = feed.Descendants(Atom + "entry");

            foreach (var entry in entries)
            {
                var externalId = entry.Element(Atom + "id")?.Value ?? "";
                if (string.IsNullOrEmpty(externalId)) continue;

                var exists = await _db.Materials
                    .AnyAsync(m => m.Source == "arxiv" && m.ExternalId == externalId);
                if (exists) continue;

                var title = entry.Element(Atom + "title")?.Value.Trim().Replace("\n", " ") ?? "";
                var summary = entry.Element(Atom + "summary")?.Value.Trim() ?? "";
                var published = entry.Element(Atom + "published")?.Value;
                int? year = DateTime.TryParse(published, out var d) ? d.Year : null;

                var pdfUrl = entry.Elements(Atom + "link")
                    .FirstOrDefault(l => (string?)l.Attribute("title") == "pdf")
                    ?.Attribute("href")?.Value;

                var primaryCategory = entry.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "primary_category")
                    ?.Attribute("term")?.Value;

                var material = new Material
                {
                    ExternalId = externalId,
                    Source = "arxiv",
                    Type = "scientific_article",
                    Title = title,
                    Summary = summary,
                    Year = year,
                    KnowledgeArea = primaryCategory,
                    PdfFileUrl = pdfUrl,
                    CopyrightFlag = false
                };

                var authorNames = entry.Elements(Atom + "author")
                    .Select(a => a.Element(Atom + "name")?.Value)
                    .Where(n => n is not null);

                foreach (var name in authorNames)
                {
                    var author = _db.Authors.Local.FirstOrDefault(x => x.Name == name)
                        ?? await _db.Authors.FirstOrDefaultAsync(x => x.Name == name);
                    if (author is null)
                    {
                        author = new Author { Name = name! };
                        _db.Authors.Add(author);
                    }
                    material.Authors.Add(author);
                }

                _db.Materials.Add(material);
            }

            await _db.SaveChangesAsync();
            await Task.Delay(3000); // intervalo mínimo exigido pelo arXiv
        }
    }
}
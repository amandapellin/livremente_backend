using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using LivreMente.Api.Models;
using LivreMente.Importer;

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var optionsBuilder = new DbContextOptionsBuilder<LivreMenteDbContext>();
optionsBuilder.UseNpgsql(config.GetConnectionString("DefaultConnection"));

using var db = new LivreMenteDbContext(optionsBuilder.Options);
using var http = new HttpClient();

await new GutendexImporter(http, db).ImportAsync();

var arxivCategories = new[]
{
    "cat:astro-ph*", "cat:cond-mat*", "cat:cs*", "cat:econ*", "cat:eess*",
    "cat:gr-qc*", "cat:hep-ex*", "cat:hep-lat*", "cat:hep-ph*", "cat:hep-th*",
    "cat:math*", "cat:math-ph*", "cat:nlin*", "cat:nucl-ex*", "cat:nucl-th*",
    "cat:physics*", "cat:q-bio*", "cat:q-fin*", "cat:quant-ph*", "cat:stat*"
};

foreach (var category in arxivCategories)
{
    await new ArxivImporter(http, db).ImportAsync(searchQuery: category, totalResults: 150);
}

Console.WriteLine("Importação concluída.");
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;
using LivreMente.Api.Models;

namespace LivreMente.Api.Services;

/// <summary>
/// Constrói o índice TF-IDF das obras no startup (RF27, #36). Best-effort: nunca
/// derruba a aplicação; enquanto não termina, o serviço de recomendação cai em
/// popularidade. `Recommendation:IndexEnabled` (default true) liga/desliga;
/// `Recommendation:IndexMaxItems` (default 30000, por download_count desc) limita o
/// corpus para caber em memória e manter o startup aceitável.
/// </summary>
public class RecommendationIndexBuilder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IRecommendationIndex index,
    ILogger<RecommendationIndexBuilder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(configuration.GetValue<bool?>("Recommendation:IndexEnabled") ?? true))
            return;
        var max = configuration.GetValue<int?>("Recommendation:IndexMaxItems") ?? 30000;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // deixa a app subir

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LivreMenteDbContext>();

            var rows = await db.Publications.AsNoTracking()
                .OrderByDescending(p => p.DownloadCount ?? 0)
                .ThenBy(p => p.Id)
                .Take(max)
                .Select(p => new
                {
                    p.Id,
                    p.Type,
                    p.Title,
                    p.Summary,
                    p.KnowledgeArea,
                    Genres = p.Genres.Select(g => g.Name).ToList(),
                })
                .ToListAsync(stoppingToken);

            if (rows.Count == 0)
            {
                logger.LogWarning("Índice de recomendação: nenhuma publicação para indexar.");
                return;
            }

            var ml = new MLContext(seed: 0);
            var docs = rows.ConvertAll(r => new PublicationTextRow
            {
                Id = r.Id,
                Text = BuildText(r.Title, r.Summary, r.Genres, r.KnowledgeArea),
            });
            var data = ml.Data.LoadFromEnumerable(docs);

            // TF-IDF com bigramas, sem n-gramas de caractere (que explodem o vocabulário),
            // minúsculas, sem pontuação/acentos; L2 (cosseno = produto interno).
            var options = new TextFeaturizingEstimator.Options
            {
                CaseMode = TextNormalizingEstimator.CaseMode.Lower,
                KeepDiacritics = false,
                KeepPunctuations = false,
                KeepNumbers = false,
                Norm = TextFeaturizingEstimator.NormFunction.L2,
                CharFeatureExtractor = null,
                WordFeatureExtractor = new WordBagEstimator.Options
                {
                    NgramLength = 2,
                    UseAllLengths = true,
                    MaximumNgramsCount = [50000],
                    Weighting = NgramExtractingEstimator.WeightingCriteria.TfIdf,
                },
            };

            var model = ml.Transforms.Text.FeaturizeText("Features", options, nameof(PublicationTextRow.Text)).Fit(data);
            var transformed = model.Transform(data);

            var vectors = new Dictionary<int, SparseVector>(rows.Count);
            var idCol = transformed.Schema["Id"];
            var featCol = transformed.Schema["Features"];
            using (var cursor = transformed.GetRowCursor(new[] { idCol, featCol }))
            {
                var idGetter = cursor.GetGetter<int>(idCol);
                var featGetter = cursor.GetGetter<VBuffer<float>>(featCol);
                var id = 0;
                VBuffer<float> buf = default;
                while (cursor.MoveNext())
                {
                    idGetter(ref id);
                    featGetter(ref buf);
                    vectors[id] = SparseVector.FromVBuffer(buf);
                }
            }

            var types = rows.ToDictionary(r => r.Id, r => r.Type);
            index.Load(ml, model, vectors, types);
            logger.LogInformation("Índice de recomendação pronto: {Count} obras indexadas.", vectors.Count);
        }
        catch (OperationCanceledException)
        {
            // encerrando
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao construir o índice de recomendação (segue por popularidade).");
        }
    }

    // Documento da obra: título + sinopse + gêneros + área (base do TF-IDF).
    private static string BuildText(string title, string? summary, List<string> genres, string? knowledgeArea)
    {
        var parts = new List<string>(4) { title };
        if (!string.IsNullOrWhiteSpace(summary)) parts.Add(summary);
        if (genres.Count > 0) parts.Add(string.Join(' ', genres));
        if (!string.IsNullOrWhiteSpace(knowledgeArea)) parts.Add(knowledgeArea);
        return string.Join(' ', parts);
    }
}

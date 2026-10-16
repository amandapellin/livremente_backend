using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>
/// Pré-aquece o cache dos arquivos EPUB dos livros mais populares no startup,
/// para a primeira abertura do leitor ser rápida. Best-effort: nunca derruba a
/// aplicação. Nº de livros em `Reading:CacheWarmCount` (0 desliga).
/// </summary>
public class PopularBooksCacheWarmer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PopularBooksCacheWarmer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var count = configuration.GetValue<int?>("Reading:CacheWarmCount") ?? 12;
        if (count <= 0)
            return;

        try
        {
            // Deixa a aplicação terminar de subir antes de começar.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LivreMenteDbContext>();
            var reading = scope.ServiceProvider.GetRequiredService<IReadingService>();

            var ids = await db.Publications.AsNoTracking()
                .Where(p => p.Type == PublicationType.book && p.EpubFileUrl != null)
                .OrderByDescending(p => p.DownloadCount)
                .Take(count)
                .Select(p => p.Id)
                .ToListAsync(stoppingToken);

            logger.LogInformation("Pré-aquecendo o cache de {Count} livros populares.", ids.Count);

            foreach (var id in ids)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;
                try
                {
                    await reading.GetFileAsync(id, PublicationFileKind.Epub, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Falha ao pré-aquecer o livro {Id}.", id);
                }
                // Gentil com o Gutenberg (evita rate-limit por rajada).
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            logger.LogInformation("Pré-aquecimento do cache concluído.");
        }
        catch (OperationCanceledException)
        {
            // Aplicação encerrando — tudo bem.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pré-aquecimento do cache de leitura falhou.");
        }
    }
}

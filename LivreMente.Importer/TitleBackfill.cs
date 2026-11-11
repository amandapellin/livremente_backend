using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Importing;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Importer;

/// <summary>
/// Limpeza única dos títulos de artigos já importados (issue #85). Reaplica o
/// <see cref="TitleSanitizer"/> às linhas existentes do arXiv e grava só as que mudaram.
/// É DML puro (UPDATE), então roda com o role da aplicação — não exige admin, não altera
/// esquema e não reimporta da fonte. Idempotente: rodar de novo não muda mais nada.
///
/// Uso: <c>dotnet run --project LivreMente.Importer -- --clean-titles</c>
/// (acrescente <c>--dry-run</c> para só listar o que mudaria, sem gravar).
/// </summary>
public static class TitleBackfill
{
    public static async Task RunAsync(LivreMenteDbContext db, bool dryRun)
    {
        Console.WriteLine(dryRun
            ? "Backfill de títulos (#85) — DRY RUN (nada será gravado)…"
            : "Backfill de títulos (#85)…");

        var publications = await db.Publications
            .Where(p => p.Source == PublicationSource.arxiv)
            .ToListAsync();

        Console.WriteLine($"Artigos carregados: {publications.Count}");

        int changed = 0, shown = 0;
        foreach (var p in publications)
        {
            var cleaned = TitleSanitizer.Clean(p.Title);
            if (cleaned == p.Title) continue;

            changed++;
            if (shown < 30) // amostra para conferência visual
            {
                Console.WriteLine($"  [{p.Id}] \"{p.Title}\"\n        -> \"{cleaned}\"");
                shown++;
            }

            if (!dryRun) p.Title = cleaned;
        }

        if (dryRun)
        {
            Console.WriteLine($"DRY RUN: {changed} título(s) mudariam (de {publications.Count}).");
            return;
        }

        if (changed == 0)
        {
            Console.WriteLine("Nenhum título a alterar — nada a fazer.");
            return;
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"Backfill concluído: {changed} título(s) atualizado(s).");
    }
}

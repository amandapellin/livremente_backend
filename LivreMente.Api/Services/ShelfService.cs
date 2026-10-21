using System.Globalization;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>
/// Estante do usuário (RF05/RF06, #32/#33). Opera sempre sobre o usuário do token
/// (RN04 por construção); um estado por obra por construção
/// (`uq_shelf_user_publication` — RN05).
/// </summary>
public class ShelfService(LivreMenteDbContext db) : IShelfService
{
    private readonly LivreMenteDbContext _db = db;

    public async Task<IReadOnlyList<ShelfItemDto>> GetShelfAsync(
        int userId, ReadingStatus? status, CancellationToken ct = default)
    {
        var query = _db.Shelves.AsNoTracking().Where(s => s.UserId == userId);
        if (status is ReadingStatus st)
            query = query.Where(s => s.Status == st);

        // Join estante → publicação reaproveitando a mesma projeção do card do
        // catálogo (ordena por título, desempate por id — estável). Projeta também
        // as URLs de arquivo para derivar o formato (EPUB/PDF) do card da Lista (#27).
        var rows = await query
            .OrderBy(s => s.Publication.Title)
            .ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Status,
                s.ReadPercentage,
                s.LastPageRead,
                s.Publication.Id,
                s.Publication.Title,
                s.Publication.Type,
                s.Publication.Source,
                s.Publication.Language,
                s.Publication.Year,
                s.Publication.CoverUrl,
                s.Publication.KnowledgeArea,
                s.Publication.EpubFileUrl,
                s.Publication.PdfFileUrl,
                Authors = s.Publication.Authors.OrderBy(a => a.Name).Select(a => a.Name).ToList(),
                Genres = s.Publication.Genres.Select(g => g.Name).ToList(),
            })
            .ToListAsync(ct);

        // Tempo de leitura ATIVA por obra (RF20) para o card da Lista (#27): soma o
        // `active_seconds` das sessões, agrupado por publicação, numa ÚNICA consulta
        // (evita N+1). Mesma fórmula de minutos do #20 (PublicationService): mínimo de
        // 1 min quando houve leitura; null quando nunca leu.
        var pubIds = rows.Select(r => r.Id).ToList();
        var secondsByPub = pubIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.ReadingSessions.AsNoTracking()
                .Where(rs => rs.UserId == userId && pubIds.Contains(rs.PublicationId))
                .GroupBy(rs => rs.PublicationId)
                .Select(g => new { PublicationId = g.Key, Total = g.Sum(rs => (int?)rs.ActiveSeconds) ?? 0 })
                .ToDictionaryAsync(x => x.PublicationId, x => x.Total, ct);

        // Tradução de slugs + tempo/formato em memória, só para os itens da estante.
        return rows.Select(r =>
        {
            var totalSeconds = secondsByPub.GetValueOrDefault(r.Id);
            int? readingTimeMinutes = totalSeconds > 0 ? Math.Max(1, (int)Math.Round(totalSeconds / 60.0)) : null;
            var format = r.EpubFileUrl is not null ? "epub" : r.PdfFileUrl is not null ? "pdf" : null;

            return new ShelfItemDto(
                new PublicationCardDto(
                    r.Id.ToString(CultureInfo.InvariantCulture),
                    r.Title, r.Type.ToString(), r.Source.ToString(), r.Language, r.Year,
                    PublicationCardMapper.CoverFor(r.Type, r.CoverUrl),
                    r.Authors,
                    PublicationCardMapper.GenreSlugs(r.Type, r.Genres, r.KnowledgeArea)),
                r.Status, r.ReadPercentage, r.LastPageRead,
                readingTimeMinutes, format);
        })
        .ToList();
    }

    public async Task<bool> SetStatusAsync(
        int userId, int publicationId, ReadingStatus status, CancellationToken ct = default)
    {
        // Publicação precisa existir (FK) — senão 404 limpo em vez de violação de FK.
        var exists = await _db.Publications.AsNoTracking().AnyAsync(p => p.Id == publicationId, ct);
        if (!exists)
            return false;

        // Upsert atômico (ON CONFLICT), mesma proteção contra corrida dos auto-saves
        // do leitor: cria o registro se a obra não está na estante (RN05 pelo
        // uq_shelf_user_publication); no conflito atualiza SÓ o status, preservando
        // read_percentage / last_page_read / bookmarked_page. O estado chega como o
        // label do enum (os membros do enum são os labels do Postgres).
        var label = status.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO shelf (user_id, publication_id, status)
            VALUES ({userId}, {publicationId}, {label}::reading_status_enum)
            ON CONFLICT (user_id, publication_id)
            DO UPDATE SET status = EXCLUDED.status", ct);

        return true;
    }

    public async Task<bool> RemoveAsync(int userId, int publicationId, CancellationToken ct = default)
    {
        var affected = await _db.Shelves
            .Where(s => s.UserId == userId && s.PublicationId == publicationId)
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }
}

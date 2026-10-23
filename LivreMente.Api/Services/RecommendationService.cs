using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Mappings;
using LivreMente.Api.Models;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Services;

/// <summary>
/// Recomendação personalizada (RF27, #36) — abordagem <b>content-based com ML</b>:
/// recomenda obras parecidas com o "gosto" do usuário, medido por similaridade de
/// cosseno sobre vetores <b>TF-IDF</b> (título + sinopse + gêneros + área), do
/// <see cref="IRecommendationIndex"/>. O vetor do usuário combina o <b>histórico</b>
/// (obras read/reading) e um pseudo-documento das <b>preferências declaradas</b>
/// (user_preference + user_genre). Exclui o que está na estante; sem perfil (ou com
/// o índice ainda carregando) cai em <b>popularidade</b> (cold start).
/// </summary>
public class RecommendationService(LivreMenteDbContext db, IRecommendationIndex index) : IRecommendationService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IRecommendationIndex _index = index;

    public async Task<RecommendationsDto> GetRecommendationsAsync(int userId, int limit, CancellationToken ct = default)
    {
        // 1) Estante: tudo é excluído; read/reading é o histórico (sinal de gosto).
        var shelf = await _db.Shelves.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.PublicationId, s.Status })
            .ToListAsync(ct);
        var shelfIds = shelf.Select(s => s.PublicationId).ToHashSet();
        var historyIds = shelf
            .Where(s => s.Status == ReadingStatus.read || s.Status == ReadingStatus.reading)
            .Select(s => s.PublicationId)
            .ToList();

        // 2) Preferências declaradas (para o pseudo-documento e o filtro de tipo).
        var prefs = await _db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.PreferenceType, p.PreferenceValue })
            .ToListAsync(ct);
        var genrePrefs = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.Genres.Select(g => g.Name))
            .ToListAsync(ct);
        var areaArchives = prefs.Where(p => p.PreferenceType == PreferenceType.knowledge_area).Select(p => p.PreferenceValue).ToList();
        var contentTypes = prefs.Where(p => p.PreferenceType == PreferenceType.content_type)
            .Select(p => Enum.TryParse<PublicationType>(p.PreferenceValue, out var t) ? (PublicationType?)t : null)
            .Where(t => t is not null).Select(t => t!.Value).ToHashSet();

        var hasProfile = historyIds.Count > 0 || genrePrefs.Count > 0 || areaArchives.Count > 0 || contentTypes.Count > 0;

        // 3) Vetor de gosto do usuário = histórico (vetores das obras read/reading) +
        //    pseudo-documento das preferências (gêneros + áreas, mesmo vocabulário do TF-IDF).
        var parts = new List<SparseVector>();
        if (_index.IsReady)
        {
            foreach (var id in historyIds)
                if (_index.Vectors.TryGetValue(id, out var v)) parts.Add(v);

            var prefText = string.Join(' ', genrePrefs.Concat(areaArchives));
            if (!string.IsNullOrWhiteSpace(prefText))
            {
                var pv = _index.Vectorize(prefText);
                if (!pv.IsEmpty) parts.Add(pv);
            }
        }
        var userVector = SparseVector.Centroid(parts);

        // 4) Sem índice pronto ou sem vetor de gosto → popularidade (cold start/fallback).
        if (!_index.IsReady || userVector.IsEmpty)
        {
            var popular = await PopularAsync(shelfIds, contentTypes, limit, ct);
            return new RecommendationsDto(!hasProfile, popular);
        }

        // 5) Pontua os candidatos (fora da estante, tipo preferido se houver) por cosseno.
        var scored = new List<(int Id, float Score)>();
        foreach (var (id, vec) in _index.Vectors)
        {
            if (shelfIds.Contains(id)) continue;
            if (contentTypes.Count > 0 && (!_index.Types.TryGetValue(id, out var type) || !contentTypes.Contains(type))) continue;
            var score = userVector.Dot(vec);
            if (score > 0f) scored.Add((id, score));
        }
        var topIds = scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => x.Id)
            .ToList();

        var items = await CardsByIdsAsync(topIds, ct);

        // 6) Top-up por popularidade quando a similaridade não encheu o limite (nicho).
        if (items.Count < limit)
        {
            var exclude = new HashSet<int>(shelfIds);
            foreach (var id in topIds) exclude.Add(id);
            var fill = await PopularAsync(exclude, contentTypes, limit - items.Count, ct);
            items.AddRange(fill);
        }

        return new RecommendationsDto(false, items);
    }

    // Obras mais populares (download_count), fora de `excludeIds` e do tipo preferido (se houver).
    private async Task<List<PublicationCardDto>> PopularAsync(
        HashSet<int> excludeIds, HashSet<PublicationType> types, int take, CancellationToken ct)
    {
        if (take <= 0) return [];
        var q = _db.Publications.AsNoTracking().Where(p => !excludeIds.Contains(p.Id));
        if (types.Count > 0) q = q.Where(p => types.Contains(p.Type));
        var rows = await q
            .OrderByDescending(p => p.DownloadCount ?? 0)
            .ThenBy(p => p.Id)
            .Take(take)
            .Select(ToCardRow)
            .AsSplitQuery()
            .ToListAsync(ct);
        return rows.Select(ToCard).ToList();
    }

    // Cards de um conjunto de ids, preservando a ordem recebida (ranking).
    private async Task<List<PublicationCardDto>> CardsByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await _db.Publications.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(ToCardRow)
            .AsSplitQuery()
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        var result = new List<PublicationCardDto>(ids.Count);
        foreach (var id in ids)
            if (byId.TryGetValue(id, out var r)) result.Add(ToCard(r));
        return result;
    }

    private sealed record CardRow(
        int Id, string Title, PublicationType Type, PublicationSource Source,
        string? Language, int? Year, string? CoverUrl, string? KnowledgeArea,
        List<string> Authors, List<string> Genres);

    private static readonly Expression<Func<Publication, CardRow>> ToCardRow = p => new CardRow(
        p.Id, p.Title, p.Type, p.Source, p.Language, p.Year, p.CoverUrl, p.KnowledgeArea,
        p.Authors.OrderBy(a => a.Name).Select(a => a.Name).ToList(),
        p.Genres.Select(g => g.Name).ToList());

    private static PublicationCardDto ToCard(CardRow r) => new(
        r.Id.ToString(CultureInfo.InvariantCulture),
        r.Title, r.Type.ToString(), r.Source.ToString(), r.Language, r.Year,
        PublicationCardMapper.CoverFor(r.Type, r.CoverUrl),
        r.Authors,
        PublicationCardMapper.GenreSlugs(r.Type, r.Genres, r.KnowledgeArea));
}

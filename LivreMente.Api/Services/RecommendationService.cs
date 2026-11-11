using System.Globalization;
using System.Linq.Expressions;
using System.Text;
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
/// <para>
/// O acervo tem <b>edições duplicadas</b> da mesma obra (vários ids para o mesmo
/// "Moby Dick"), então a exclusão da estante e a lista final são feitas por
/// <b>chave de obra</b> (título + 1º autor normalizados), não só por id. Além disso,
/// <b>edições em outro idioma</b> (título diferente, ex.: "El crimen y el castigo" ×
/// "Crime and Punishment") são detectadas por similaridade: candidato do <b>mesmo
/// 1º autor</b> cujo vetor TF-IDF tem cosseno ≥ limiar com alguma obra <b>da estante
/// ou já escolhida</b> é tratado como a mesma obra e descartado (o resumo do Gutendex
/// é em inglês, então a tradução fica muito próxima do original; o gate por autor
/// evita fundir obras distintas do mesmo autor). Comparar contra as já escolhidas
/// evita duas edições em línguas diferentes da mesma obra na própria lista.
/// </para>
/// </summary>
public class RecommendationService(
    LivreMenteDbContext db,
    IRecommendationIndex index,
    IConfiguration config,
    ILogger<RecommendationService> logger) : IRecommendationService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IRecommendationIndex _index = index;
    private readonly ILogger<RecommendationService> _logger = logger;

    // Busca mais candidatos do que o limite para sobrar margem após as deduplicações.
    private const int OverfetchFactor = 6;

    // Cosseno mínimo (vetores L2 → produto interno) p/ tratar um candidato do mesmo
    // 1º autor como edição/tradução de uma obra da estante/já escolhida. Config; 0 desliga.
    // 0.35 medido no banco de dev: separa a tradução "El crimen y el castigo"×"Crime and
    // Punishment" (cosseno 0.42) das obras distintas do mesmo autor (≤ 0.23) com folga.
    private readonly float _editionSimilarity =
        config.GetValue<float?>("Recommendation:WorkEditionSimilarity") ?? 0.35f;

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

        // Publicações da estante (id + título + 1º autor) para as duas deduplicações.
        var shelfWorks = await ShelfWorksAsync(shelfIds, ct);
        // Chave de obra (título + 1º autor): exclui outras edições de mesmo título/autor.
        var shelfKeys = shelfWorks.Select(w => WorkKey(w.Title, w.FirstAuthor)).ToHashSet();
        // Vetores das obras da estante por 1º autor: exclui edições em outro idioma (título diferente).
        var shelfVecByAuthor = ShelfVectorsByAuthor(shelfWorks);

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

        // Estado das deduplicações, compartilhado entre a lista principal e o top-up:
        // chaves de obra já vistas + vetores das obras já escolhidas (por 1º autor).
        var seenKeys = new HashSet<string>();
        var acceptedVecByAuthor = new Dictionary<string, List<SparseVector>>();

        // 4) Sem índice pronto ou sem vetor de gosto → popularidade (cold start/fallback).
        if (!_index.IsReady || userVector.IsEmpty)
        {
            var popular = await PopularAsync(shelfIds, shelfKeys, shelfVecByAuthor, acceptedVecByAuthor, contentTypes, limit, ct, seenKeys);
            return new RecommendationsDto(!hasProfile, popular.Select(ToCard).ToList());
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
        // Busca com folga: a lista final deduplica por obra e por edição em outro idioma.
        var topIds = scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Id)
            .Take(limit * OverfetchFactor)
            .Select(x => x.Id)
            .ToList();

        var ranked = await CardsByIdsAsync(topIds, ct);

        // Deduplica por obra e descarta edições de obras da estante ou já escolhidas.
        var accepted = new List<CardRow>(limit);
        foreach (var row in ranked)
        {
            if (accepted.Count >= limit) break;
            if (Accept(row, shelfKeys, seenKeys, shelfVecByAuthor, acceptedVecByAuthor)) accepted.Add(row);
        }

        // 6) Top-up por popularidade quando a similaridade não encheu o limite (nicho).
        if (accepted.Count < limit)
        {
            var excludeIds = new HashSet<int>(shelfIds);
            foreach (var row in accepted) excludeIds.Add(row.Id);
            var fill = await PopularAsync(excludeIds, shelfKeys, shelfVecByAuthor, acceptedVecByAuthor, contentTypes, limit - accepted.Count, ct, seenKeys);
            accepted.AddRange(fill);
        }

        return new RecommendationsDto(false, accepted.Select(ToCard).ToList());
    }

    // Obras mais populares (download_count), fora de `excludeIds`, do tipo preferido (se houver),
    // sem repetir obra já vista e sem edições de obras da estante/já escolhidas (mesma dedup).
    private async Task<List<CardRow>> PopularAsync(
        HashSet<int> excludeIds, HashSet<string> shelfKeys,
        IReadOnlyDictionary<string, List<SparseVector>> shelfVecByAuthor,
        Dictionary<string, List<SparseVector>> acceptedVecByAuthor,
        HashSet<PublicationType> types, int take, CancellationToken ct, HashSet<string> seenKeys)
    {
        if (take <= 0) return [];
        var q = _db.Publications.AsNoTracking().Where(p => !excludeIds.Contains(p.Id));
        if (types.Count > 0) q = q.Where(p => types.Contains(p.Type));
        // Busca com folga: a dedup descarta edições repetidas da mesma obra popular.
        var rows = await q
            .OrderByDescending(p => p.DownloadCount ?? 0)
            .ThenBy(p => p.Id)
            .Take(take * OverfetchFactor + 50)
            .Select(ToCardRow)
            .AsSplitQuery()
            .ToListAsync(ct);
        var result = new List<CardRow>(take);
        foreach (var row in rows)
        {
            if (result.Count >= take) break;
            if (Accept(row, shelfKeys, seenKeys, shelfVecByAuthor, acceptedVecByAuthor)) result.Add(row);
        }
        return result;
    }

    // Decide se uma obra entra na lista: não pode ser obra já vista, nem obra da estante
    // (por chave de título+autor), nem uma edição/tradução de obra da estante OU já
    // escolhida (por similaridade do mesmo 1º autor). Ao aceitar, registra o vetor para
    // que as próximas não repitam a obra em outra língua.
    private bool Accept(
        CardRow row, HashSet<string> shelfKeys, HashSet<string> seenKeys,
        IReadOnlyDictionary<string, List<SparseVector>> shelfVecByAuthor,
        Dictionary<string, List<SparseVector>> acceptedVecByAuthor)
    {
        var firstAuthor = row.Authors.Count > 0 ? row.Authors[0] : null;
        var authorKey = Normalize(firstAuthor);
        var key = Normalize(row.Title) + "|" + authorKey;
        if (shelfKeys.Contains(key) || !seenKeys.Add(key)) return false;

        if (_editionSimilarity > 0f && _index.Vectors.TryGetValue(row.Id, out var cv) && !cv.IsEmpty)
        {
            var best = Math.Max(
                BestCosine(authorKey, cv, shelfVecByAuthor),
                BestCosine(authorKey, cv, acceptedVecByAuthor));
            if (best >= _editionSimilarity)
            {
                _logger.LogDebug("Rec: exclui edição de obra id={Id} '{Title}' (cosseno {Score:F3})", row.Id, row.Title, best);
                return false;
            }
            // Obra aceita: guarda o vetor para deduplicar edições em outra língua na própria lista.
            if (authorKey.Length > 0)
            {
                if (!acceptedVecByAuthor.TryGetValue(authorKey, out var list)) acceptedVecByAuthor[authorKey] = list = [];
                list.Add(cv);
            }
        }
        return true;
    }

    // Maior cosseno entre `vec` e os vetores do mesmo 1º autor no mapa (0 se não houver).
    private static float BestCosine(string authorKey, SparseVector vec, IReadOnlyDictionary<string, List<SparseVector>> byAuthor)
    {
        if (authorKey.Length == 0 || !byAuthor.TryGetValue(authorKey, out var vecs)) return 0f;
        var best = 0f;
        foreach (var sv in vecs)
        {
            var s = vec.Dot(sv); // vetores do índice são L2 → produto interno = cosseno
            if (s > best) best = s;
        }
        return best;
    }

    // Vetores das obras da estante (presentes no índice) agrupados pelo 1º autor normalizado.
    private Dictionary<string, List<SparseVector>> ShelfVectorsByAuthor(
        IReadOnlyList<(int Id, string Title, string? FirstAuthor)> shelfWorks)
    {
        var map = new Dictionary<string, List<SparseVector>>();
        if (!_index.IsReady) return map;
        foreach (var w in shelfWorks)
        {
            if (!_index.Vectors.TryGetValue(w.Id, out var v) || v.IsEmpty) continue;
            var ak = Normalize(w.FirstAuthor);
            if (ak.Length == 0) continue;
            if (!map.TryGetValue(ak, out var list)) map[ak] = list = [];
            list.Add(v);
        }
        return map;
    }

    // Linhas de card de um conjunto de ids, preservando a ordem recebida (ranking).
    private async Task<List<CardRow>> CardsByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await _db.Publications.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(ToCardRow)
            .AsSplitQuery()
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        var result = new List<CardRow>(ids.Count);
        foreach (var id in ids)
            if (byId.TryGetValue(id, out var r)) result.Add(r);
        return result;
    }

    // Publicações da estante com id + título + 1º autor (p/ chave de obra e vetores por autor).
    private async Task<List<(int Id, string Title, string? FirstAuthor)>> ShelfWorksAsync(HashSet<int> shelfIds, CancellationToken ct)
    {
        if (shelfIds.Count == 0) return [];
        var rows = await _db.Publications.AsNoTracking()
            .Where(p => shelfIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, FirstAuthor = p.Authors.OrderBy(a => a.Name).Select(a => a.Name).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Title, r.FirstAuthor)).ToList();
    }

    // Identidade de "obra" para deduplicar edições de mesmo título: título + 1º autor,
    // normalizados (sem acentos, só alfanuméricos, minúsculo) — casa "Moby Dick; Or, The
    // Whale" com "Moby-Dick; or, The Whale" (mesmo autor), que só diferem em pontuação/caixa.
    private static string WorkKey(string? title, string? firstAuthor)
        => Normalize(title) + "|" + Normalize(firstAuthor);

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
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

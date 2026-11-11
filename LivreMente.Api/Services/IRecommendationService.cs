namespace LivreMente.Api.Services;

public interface IRecommendationService
{
    /// <summary>
    /// Recomendações personalizadas (RF27, #36) para o usuário: considera as
    /// preferências declaradas (user_preference + user_genre) e o histórico de
    /// leitura (estante com estado read/reading), exclui o que já está na estante e,
    /// sem perfil nem histórico, cai em popularidade (cold start). Devolve até
    /// <paramref name="limit"/> cards.
    /// </summary>
    Task<Dtos.RecommendationsDto> GetRecommendationsAsync(int userId, int limit, CancellationToken ct = default);
}

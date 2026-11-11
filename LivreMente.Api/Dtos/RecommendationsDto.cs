namespace LivreMente.Api.Dtos;

/// <summary>
/// Recomendações personalizadas (RF27, #36). Os itens são sempre obras que o
/// usuário <b>não</b> tem na estante, no mesmo <see cref="PublicationCardDto"/> do
/// catálogo. <see cref="ColdStart"/> é <c>true</c> quando o usuário não tem
/// preferências nem histórico de leitura (read/reading): a lista cai em
/// popularidade (download_count).
/// </summary>
public record RecommendationsDto(bool ColdStart, IReadOnlyList<PublicationCardDto> Items);

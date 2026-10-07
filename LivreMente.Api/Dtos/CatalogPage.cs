using System.Text.Json.Serialization;

namespace LivreMente.Api.Dtos;

public record CatalogPage(
    IReadOnlyList<PublicationCardDto> Items,
    int Page,
    int PageSize,
    int Total,
    int TotalPages,
    CatalogCountsDto Counts);

public record CatalogCountsDto(
    int All,
    int Book,
    [property: JsonPropertyName("scientific_article")] int ScientificArticle);

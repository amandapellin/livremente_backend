using LivreMente.Api.Services;

namespace LivreMente.Api.Endpoints;

/// <summary>
/// Endpoints do recurso "gêneros". Cada handler é fino: recebe a request,
/// chama o service e devolve o resultado. Nenhuma regra de negócio aqui.
/// </summary>
public static class GenreEndpoints
{
    public static RouteGroupBuilder MapGenreEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/genres").WithTags("Genres");

        group.MapGet("/", async (IGenreService genres, CancellationToken ct) =>
            Results.Ok(await genres.GetAllAsync(ct)));

        group.MapGet("/{id:int}", async (int id, IGenreService genres, CancellationToken ct) =>
        {
            var genre = await genres.GetByIdAsync(id, ct);
            return genre is null ? Results.NotFound() : Results.Ok(genre);
        });

        group.MapGet("/{id:int}/publications", async (
            int id, IGenreService genres, CancellationToken ct, int page = 1, int pageSize = 20) =>
            Results.Ok(await genres.GetPublicationsAsync(id, page, pageSize, ct)));

        return group;
    }
}

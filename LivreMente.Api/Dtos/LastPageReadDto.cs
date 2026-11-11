namespace LivreMente.Api.Dtos;

// Retomada de leitura (RF18): última página lida; null se o usuário nunca leu.
public record LastPageReadDto(int? LastPageRead);

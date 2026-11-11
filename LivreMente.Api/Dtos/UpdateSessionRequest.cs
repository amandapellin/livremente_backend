namespace LivreMente.Api.Dtos;

// Heartbeat/encerramento da sessão (RF20): tempo de tela ativa, em segundos (>= 0).
public record UpdateSessionRequest(int ActiveSeconds);

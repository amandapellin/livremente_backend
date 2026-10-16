namespace LivreMente.Api.Dtos;

// Atualização do percentual lido (RF19). Valor aceito entre 0 e 100.
public record UpdatePercentageRequest(int ReadPercentage);

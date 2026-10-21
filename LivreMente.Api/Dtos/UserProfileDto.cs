namespace LivreMente.Api.Dtos;

// `Gender` serializa como string (nome do membro do enum: female, male,
// non_binary, other, prefer_not_to_say); `BirthDate` como data (yyyy-MM-dd) — o
// front calcula a idade. Expostos para o card do usuário na estante (#27); já
// existem em `users` (sem coluna nova).
public record UserProfileDto(string Id, string Name, string Email, string? AvatarUrl, string Gender, DateOnly BirthDate);

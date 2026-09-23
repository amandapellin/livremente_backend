using LivreMente.Api.Dtos;

namespace LivreMente.Api.Services;

public enum ChangePasswordError
{
    None,
    UserNotFound,
    InvalidCurrentPassword,
}

public enum AvatarError
{
    None,
    UserNotFound,
}

public interface IUserService
{
    Task<UserProfileDto?> GetProfileAsync(int userId, CancellationToken ct = default);

    Task<UserProfileDto?> UpdateProfileAsync(int userId, string name, CancellationToken ct = default);

    Task<ChangePasswordError> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default);

    Task<AvatarError> SetAvatarAsync(int userId, byte[] content, string contentType, CancellationToken ct = default);

    Task<(byte[] Content, string ContentType)?> GetAvatarAsync(int userId, CancellationToken ct = default);
}

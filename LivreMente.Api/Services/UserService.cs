using Microsoft.EntityFrameworkCore;
using LivreMente.Api.Dtos;
using LivreMente.Api.Models;
using LivreMente.Api.Security;

namespace LivreMente.Api.Services;

/// <summary>
/// Regra de negócio do perfil do usuário autenticado (RF03). Opera sempre sobre
/// o próprio usuário (id vindo do token), o que atende a RN04 por construção.
/// </summary>
public class UserService(LivreMenteDbContext db, IPasswordHasher passwordHasher) : IUserService
{
    private readonly LivreMenteDbContext _db = db;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    private static DateTime Now() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    public async Task<UserProfileDto?> GetProfileAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;

        var hasAvatar = await _db.UserAvatars.AnyAsync(a => a.UserId == userId, ct);
        return ToProfile(user, hasAvatar);
    }

    public async Task<UserProfileDto?> UpdateProfileAsync(int userId, string name, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;

        user.FullName = name.Trim();
        user.UpdateDate = Now();
        await _db.SaveChangesAsync(ct);

        var hasAvatar = await _db.UserAvatars.AnyAsync(a => a.UserId == userId, ct);
        return ToProfile(user, hasAvatar);
    }

    public async Task<ChangePasswordError> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return ChangePasswordError.UserNotFound;

        if (!_passwordHasher.Verify(currentPassword, user.PasswordHash))
            return ChangePasswordError.InvalidCurrentPassword;

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.UpdateDate = Now();
        await _db.SaveChangesAsync(ct);
        return ChangePasswordError.None;
    }

    public async Task<AvatarError> SetAvatarAsync(int userId, byte[] content, string contentType, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            return AvatarError.UserNotFound;

        var avatar = await _db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (avatar is null)
        {
            _db.UserAvatars.Add(new UserAvatar
            {
                UserId = userId,
                Content = content,
                ContentType = contentType,
                UpdateDate = Now(),
            });
        }
        else
        {
            avatar.Content = content;
            avatar.ContentType = contentType;
            avatar.UpdateDate = Now();
        }

        await _db.SaveChangesAsync(ct);
        return AvatarError.None;
    }

    public async Task<(byte[] Content, string ContentType)?> GetAvatarAsync(int userId, CancellationToken ct = default)
    {
        var avatar = await _db.UserAvatars.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        return avatar is null ? null : (avatar.Content, avatar.ContentType);
    }

    private static UserProfileDto ToProfile(User user, bool hasAvatar) =>
        new(user.Id.ToString(), user.FullName, user.Email, hasAvatar ? $"/api/users/{user.Id}/avatar" : null);
}

namespace LivreMente.Api.Validation;

/// <summary>
/// Validação do avatar: aceita apenas PNG ou JPEG, detectados pelos *magic bytes*
/// (o content-type informado pelo cliente é ignorado por ser falsificável).
/// </summary>
public static class AvatarValidation
{
    public const long MaxBytes = 2 * 1024 * 1024; // 2 MB

    /// <summary>Content-type canônico se o conteúdo for PNG/JPEG; senão, null.</summary>
    public static string? DetectImageType(byte[] content)
    {
        if (IsPng(content)) return "image/png";
        if (IsJpeg(content)) return "image/jpeg";
        return null;
    }

    private static bool IsPng(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
        && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    private static bool IsJpeg(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
}

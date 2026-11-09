namespace LivreMente.Api.Common;

/// <summary>
/// Hora atual em UTC com <see cref="DateTimeKind.Unspecified"/>. As colunas são
/// `timestamp without time zone` e o Npgsql **recusa** <see cref="DateTime"/> com
/// <c>Kind=Utc</c> nelas — por isso o `SpecifyKind` para Unspecified. Fonte única
/// (antes duplicado em AuthService/ReadingService/UserService como `Now()` privado).
/// Importe com <c>using static LivreMente.Api.Common.Clock;</c> e chame <c>Now()</c>.
/// </summary>
public static class Clock
{
    public static DateTime Now() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}

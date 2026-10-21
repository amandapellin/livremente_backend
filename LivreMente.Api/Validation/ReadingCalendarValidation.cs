using System.Globalization;

namespace LivreMente.Api.Validation;

/// <summary>
/// Validação/normalização do intervalo do calendário de leitura (RF09, #35).
/// Sem parâmetros → mês atual; exige `from` e `to` juntos; `from &lt;= to`; e um
/// teto de amplitude para evitar varreduras enormes.
/// </summary>
public static class ReadingCalendarValidation
{
    private const int MaxSpanDays = 366;

    public static (string? Error, DateOnly From, DateOnly To) Resolve(string? from, string? to)
    {
        // Ambos ausentes → mês atual (conveniência do "ex.: mês atual" da issue).
        if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var first = new DateOnly(today.Year, today.Month, 1);
            return (null, first, first.AddMonths(1).AddDays(-1));
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return ("Informe 'from' e 'to' juntos (ou nenhum, para o mês atual).", default, default);

        if (!DateOnly.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return ("'from' inválido; use o formato yyyy-MM-dd.", default, default);
        if (!DateOnly.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return ("'to' inválido; use o formato yyyy-MM-dd.", default, default);

        if (f > t)
            return ("'from' deve ser anterior ou igual a 'to'.", default, default);
        if (t.DayNumber - f.DayNumber + 1 > MaxSpanDays)
            return ($"O intervalo não pode exceder {MaxSpanDays} dias.", default, default);

        return (null, f, t);
    }
}

namespace LivreMente.Api.Dtos;

/// <summary>
/// Métricas agregadas de leitura do usuário (RF27, #37 — cards da tela de
/// recomendações), derivadas da <c>reading_session</c> (tempo de tela ativa).
/// Tudo em minutos / dias; zeros quando não há leitura. Agregado por <b>dia UTC</b>
/// (o <c>start_time</c> é gravado em UTC sem fuso).
/// </summary>
public record ReadingStatsDto(
    int TotalMinutes,          // soma de todo o tempo ativo (ex.: "Total 46h")
    int TodayMinutes,          // tempo ativo de hoje (ex.: "Hoje 1h 24min")
    int DailyAverageMinutes,   // média por dia COM leitura (total / dias ativos)
    int CurrentStreakDays,     // dias consecutivos até hoje/ontem (ex.: "6 dias seguidos")
    int LongestStreakDays,     // maior sequência de dias consecutivos (ex.: "Melhor: 6 dias")
    int ActiveDays,            // total de dias distintos com leitura
    int DaysThisMonth);        // dias com leitura no mês atual (ex.: "19 dias em agosto")

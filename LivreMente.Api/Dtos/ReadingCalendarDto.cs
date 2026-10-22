namespace LivreMente.Api.Dtos;

/// <summary>
/// Calendário de frequência de leitura (RF09, #35) no intervalo [From, To]
/// (inclusivo). <see cref="Days"/> é uma lista <b>esparsa</b>: só os dias COM
/// atividade; o front preenche os dias vazios do grid do mês.
/// </summary>
public record ReadingCalendarDto(DateOnly From, DateOnly To, IReadOnlyList<ReadingCalendarDayDto> Days);

/// <summary>
/// Um dia com leitura: minutos de tela ativa (RF20, soma de
/// <c>reading_session.active_seconds</c> em minutos) e número de sessões. A
/// intensidade do heatmap é derivada no front a partir de <see cref="ActiveMinutes"/>.
/// </summary>
public record ReadingCalendarDayDto(DateOnly Date, int ActiveMinutes, int SessionCount);

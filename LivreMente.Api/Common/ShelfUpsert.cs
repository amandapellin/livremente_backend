using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using LivreMente.Api.Models.Enums;

namespace LivreMente.Api.Common;

/// <summary>
/// Upserts atômicos na tabela <c>shelf</c> (`INSERT ... ON CONFLICT (user_id,
/// publication_id) DO UPDATE`). Centraliza o padrão que se repetia nos auto-saves
/// do leitor (ReadingService) e no estado da estante (ShelfService): a inserção
/// concorrente em find-or-create quebrava o `uq_shelf_user_publication`, então
/// tudo passa por este upsert atômico.
///
/// O nome da coluna **não** é parametrizável em SQL cru; por isso vem de um
/// <see cref="ShelfProgressColumn"/> (lista branca) mapeado para um literal fixo —
/// sem concatenar entrada do usuário. Os valores (ids, número, status) vão sempre
/// como parâmetros.
/// </summary>
public static class ShelfUpsert
{
    /// <summary>Colunas de progresso que os auto-saves do leitor atualizam.</summary>
    public enum ShelfProgressColumn { LastPageRead, ReadPercentage, BookmarkedPage }

    private static string ColumnName(ShelfProgressColumn column) => column switch
    {
        ShelfProgressColumn.LastPageRead => "last_page_read",
        ShelfProgressColumn.ReadPercentage => "read_percentage",
        ShelfProgressColumn.BookmarkedPage => "bookmarked_page",
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    /// <summary>
    /// Grava um valor de progresso (última página, percentual ou marcação). Se a obra
    /// não está na estante, cria com <c>status = reading</c> (ler/marcar implica
    /// "lendo"); no conflito atualiza SÓ a coluna informada, preservando as demais.
    /// </summary>
    public static Task UpsertProgressAsync(
        DatabaseFacade db, int userId, int publicationId,
        ShelfProgressColumn column, int value, CancellationToken ct = default)
    {
        var col = ColumnName(column);
        var sql = $@"
            INSERT INTO shelf (user_id, publication_id, status, {col})
            VALUES ({{0}}, {{1}}, 'reading'::reading_status_enum, {{2}})
            ON CONFLICT (user_id, publication_id)
            DO UPDATE SET {col} = EXCLUDED.{col}";
        // Argumentos nomeados: garante a sobrecarga (sql, IEnumerable<object>, CancellationToken)
        // — o ct é o token de cancelamento, não um parâmetro SQL.
        return db.ExecuteSqlRawAsync(sql, parameters: new object[] { userId, publicationId, value }, cancellationToken: ct);
    }

    /// <summary>
    /// Define o estado da obra na estante. Cria com o estado informado se a obra não
    /// está na estante; no conflito atualiza SÓ o status, preservando o progresso
    /// (read_percentage / last_page_read / bookmarked_page) — RN05.
    /// </summary>
    public static Task UpsertStatusAsync(
        DatabaseFacade db, int userId, int publicationId,
        ReadingStatus status, CancellationToken ct = default)
    {
        // O estado trafega como o label do enum (os membros do enum C# são os labels
        // do tipo reading_status_enum do Postgres); cast explícito no SQL.
        const string sql = @"
            INSERT INTO shelf (user_id, publication_id, status)
            VALUES ({0}, {1}, {2}::reading_status_enum)
            ON CONFLICT (user_id, publication_id)
            DO UPDATE SET status = EXCLUDED.status";
        return db.ExecuteSqlRawAsync(sql, parameters: new object[] { userId, publicationId, status.ToString() }, cancellationToken: ct);
    }
}

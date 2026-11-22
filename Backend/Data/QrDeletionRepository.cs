using System;
using System.Threading.Tasks;
using Npgsql;

namespace Backend.Data;

/// <summary>Итог атомарного удаления QR-кода и связанных данных.</summary>
public sealed record QrDeletionResult(
    bool QrFound,
    bool BoxFound,
    int DeletedRunItems,
    int DeletedRuns,
    int DeletedChecklistItems,
    int DeletedPhotos,
    int DeletedEvents);

/// <summary>
/// Удаляет QR-код и его историю одной транзакцией, чтобы ошибка не оставляла
/// базу в частично очищенном состоянии.
/// </summary>
public class QrDeletionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public QrDeletionRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Удаляет QR-код, коробку (если она есть), содержимое и историю инвентаризаций.
    /// Если requireBox равен true, отсутствие коробки отменяет транзакцию.
    /// </summary>
    public async Task<QrDeletionResult> DeleteQrAndDependenciesAsync(long qrCodeId, bool requireBox)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // Блокируем QR на время удаления, чтобы параллельный запрос не изменил его.
        await using var qrCmd = new NpgsqlCommand(
            "SELECT id FROM qr_codes WHERE id = @qrCodeId FOR UPDATE", conn, transaction);
        qrCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        if (await qrCmd.ExecuteScalarAsync() is null)
        {
            await transaction.RollbackAsync();
            return new QrDeletionResult(false, false, 0, 0, 0, 0, 0);
        }

        // Сначала находим коробку, чтобы удалить её историю и содержимое в одной транзакции.
        long? boxId;
        await using (var boxCmd = new NpgsqlCommand(
            "SELECT id FROM boxes WHERE qr_code_id = @qrCodeId FOR UPDATE", conn, transaction))
        {
            boxCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            var boxResult = await boxCmd.ExecuteScalarAsync();
            boxId = boxResult is null ? null : Convert.ToInt64(boxResult);
        }

        if (requireBox && boxId is null)
        {
            await transaction.RollbackAsync();
            return new QrDeletionResult(true, false, 0, 0, 0, 0, 0);
        }

        // Отметки ссылаются на чек-лист с RESTRICT, поэтому их удаляем до истории.
        var boxRunFilter = boxId.HasValue ? " OR run.box_id = @boxId" : string.Empty;
        int deletedRunItems;
        await using (var deleteRunItemsCmd = new NpgsqlCommand($@"
            DELETE FROM inventory_run_items item
            USING inventory_runs run
            WHERE item.inventory_run_id = run.id
              AND (run.qr_code_id = @qrCodeId{boxRunFilter})", conn, transaction))
        {
            deleteRunItemsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (boxId.HasValue)
                deleteRunItemsCmd.Parameters.AddWithValue("boxId", boxId.Value);
            deletedRunItems = await deleteRunItemsCmd.ExecuteNonQueryAsync();
        }

        // Удаление истории каскадно удалит её медиа-вложения и остальные дочерние записи.
        var boxDeleteFilter = boxId.HasValue ? " OR box_id = @boxId" : string.Empty;
        int deletedRuns;
        await using (var deleteRunsCmd = new NpgsqlCommand(
            $"DELETE FROM inventory_runs WHERE qr_code_id = @qrCodeId{boxDeleteFilter}", conn, transaction))
        {
            deleteRunsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (boxId.HasValue)
                deleteRunsCmd.Parameters.AddWithValue("boxId", boxId.Value);
            deletedRuns = await deleteRunsCmd.ExecuteNonQueryAsync();
        }

        int deletedChecklistItems = 0;
        int deletedPhotos = 0;
        int deletedEvents = 0;

        if (boxId.HasValue)
        {
            await using (var deleteChecklistCmd = new NpgsqlCommand(
                "DELETE FROM box_items WHERE box_id = @boxId", conn, transaction))
            {
                deleteChecklistCmd.Parameters.AddWithValue("boxId", boxId.Value);
                deletedChecklistItems = await deleteChecklistCmd.ExecuteNonQueryAsync();
            }

            await using (var deletePhotosCmd = new NpgsqlCommand(
                "DELETE FROM box_photos WHERE box_id = @boxId", conn, transaction))
            {
                deletePhotosCmd.Parameters.AddWithValue("boxId", boxId.Value);
                deletedPhotos = await deletePhotosCmd.ExecuteNonQueryAsync();
            }

            await using (var deleteEventsCmd = new NpgsqlCommand(
                "DELETE FROM inventory_sessions WHERE qr_code_id = @qrCodeId", conn, transaction))
            {
                deleteEventsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
                deletedEvents = await deleteEventsCmd.ExecuteNonQueryAsync();
            }

            await using var deleteBoxCmd = new NpgsqlCommand(
                "DELETE FROM boxes WHERE id = @boxId", conn, transaction);
            deleteBoxCmd.Parameters.AddWithValue("boxId", boxId.Value);
            await deleteBoxCmd.ExecuteNonQueryAsync();
        }

        // Сам QR удаляем последним, но до COMMIT той же транзакции.
        await using (var deleteQrCmd = new NpgsqlCommand(
            "DELETE FROM qr_codes WHERE id = @qrCodeId", conn, transaction))
        {
            deleteQrCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (await deleteQrCmd.ExecuteNonQueryAsync() == 0)
                throw new InvalidOperationException("QR-код исчез во время удаления.");
        }

        await transaction.CommitAsync();
        return new QrDeletionResult(
            true, boxId.HasValue, deletedRunItems, deletedRuns,
            deletedChecklistItems, deletedPhotos, deletedEvents);
    }
}

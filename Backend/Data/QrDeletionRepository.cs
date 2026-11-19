using System;
using System.Threading.Tasks;
using Npgsql;

namespace Backend.Data;

/// <summary>Итог атомарного удаления QR-кода и связанных данных.</summary>
public sealed record QrDeletionResult(
    bool QrFound,
    bool CabinetFound,
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
    /// Удаляет QR-код, шкаф (если он есть), чек-лист и историю инвентаризаций.
    /// Если requireCabinet равен true, отсутствие шкафа отменяет транзакцию.
    /// </summary>
    public async Task<QrDeletionResult> DeleteQrAndDependenciesAsync(long qrCodeId, bool requireCabinet)
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

        // Сначала узнаём шкаф, чтобы связанные с ним данные удалить в том же порядке.
        long? cabinetId;
        await using (var cabinetCmd = new NpgsqlCommand(
            "SELECT id FROM cabinet_profiles WHERE qr_code_id = @qrCodeId FOR UPDATE", conn, transaction))
        {
            cabinetCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            var cabinetResult = await cabinetCmd.ExecuteScalarAsync();
            cabinetId = cabinetResult is null ? null : Convert.ToInt64(cabinetResult);
        }

        if (requireCabinet && cabinetId is null)
        {
            await transaction.RollbackAsync();
            return new QrDeletionResult(true, false, 0, 0, 0, 0, 0);
        }

        // Отметки ссылаются на чек-лист с RESTRICT, поэтому их удаляем до истории.
        var cabinetRunFilter = cabinetId.HasValue ? " OR run.cabinet_id = @cabinetId" : string.Empty;
        int deletedRunItems;
        await using (var deleteRunItemsCmd = new NpgsqlCommand($@"
            DELETE FROM inventory_run_items item
            USING inventory_runs run
            WHERE item.inventory_run_id = run.id
              AND (run.qr_code_id = @qrCodeId{cabinetRunFilter})", conn, transaction))
        {
            deleteRunItemsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (cabinetId.HasValue)
                deleteRunItemsCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deletedRunItems = await deleteRunItemsCmd.ExecuteNonQueryAsync();
        }

        // Удаление истории каскадно удалит её медиа-вложения и остальные дочерние записи.
        var cabinetDeleteFilter = cabinetId.HasValue ? " OR cabinet_id = @cabinetId" : string.Empty;
        int deletedRuns;
        await using (var deleteRunsCmd = new NpgsqlCommand(
            $"DELETE FROM inventory_runs WHERE qr_code_id = @qrCodeId{cabinetDeleteFilter}", conn, transaction))
        {
            deleteRunsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (cabinetId.HasValue)
                deleteRunsCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deletedRuns = await deleteRunsCmd.ExecuteNonQueryAsync();
        }

        int deletedChecklistItems = 0;
        int deletedPhotos = 0;
        int deletedEvents = 0;

        if (cabinetId.HasValue)
        {
            await using (var deleteChecklistCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_checklist_items WHERE cabinet_id = @cabinetId", conn, transaction))
            {
                deleteChecklistCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
                deletedChecklistItems = await deleteChecklistCmd.ExecuteNonQueryAsync();
            }

            await using (var deletePhotosCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_photos WHERE cabinet_id = @cabinetId", conn, transaction))
            {
                deletePhotosCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
                deletedPhotos = await deletePhotosCmd.ExecuteNonQueryAsync();
            }

            await using (var deleteEventsCmd = new NpgsqlCommand(
                "DELETE FROM inventory_sessions WHERE qr_code_id = @qrCodeId", conn, transaction))
            {
                deleteEventsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
                deletedEvents = await deleteEventsCmd.ExecuteNonQueryAsync();
            }

            await using var deleteCabinetCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_profiles WHERE id = @cabinetId", conn, transaction);
            deleteCabinetCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            await deleteCabinetCmd.ExecuteNonQueryAsync();
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
            true, cabinetId.HasValue, deletedRunItems, deletedRuns,
            deletedChecklistItems, deletedPhotos, deletedEvents);
    }
}

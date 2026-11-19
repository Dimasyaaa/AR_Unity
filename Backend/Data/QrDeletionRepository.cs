using System;
using System.Threading.Tasks;
using Npgsql;

namespace Backend.Data;

/// <summary>
/// Слой для сложного транзакционного удаления QR-кодов.
/// Вынесен отдельно, потому что удаление QR может требовать каскадного удаления 
/// связанного шкафа (cabinet_profiles), его чек-листов, фото и истории инвентаризаций.
/// Это точная копия логики метода DeleteQrDependencies из старого InventoryApi.cs.
/// </summary>
public class QrDeletionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public QrDeletionRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Транзакционное удаление зависимостей QR-кода.
    /// </summary>
    /// <param name="qrCodeId">ID удаляемого QR-кода.</param>
    /// <param name="requireCabinet">Если true, и шкафа нет, операция отменяется (возвращает null).</param>
    /// <returns>Кортеж с количеством удаленных записей по таблицам, или null, если операция прервана.</returns>
    public async Task<(long? CabinetId, int DeletedRunItems, int DeletedRuns, int DeletedChecklistItems, int DeletedPhotos, int DeletedEvents)?> 
        DeleteQrDependenciesAsync(long qrCodeId, bool requireCabinet)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        long? cabinetId = null;

        // 1. Проверяем, привязан ли к этому QR-коду шкаф
        await using var cabinetCmd = new NpgsqlCommand(
            "SELECT id FROM cabinet_profiles WHERE qr_code_id = @qrCodeId", conn, transaction);
        cabinetCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        var cabinetResult = await cabinetCmd.ExecuteScalarAsync();
        
        if (cabinetResult != null)
            cabinetId = Convert.ToInt64(cabinetResult);

        // Если шкаф обязателен, но его нет — откатываем транзакцию и возвращаем null
        if (requireCabinet && cabinetId is null)
        {
            await transaction.RollbackAsync();
            return null;
        }

        // 2. Удаляем элементы инвентаризации (inventory_run_items), связанные с этим QR или шкафом
        var cabinetRunFilter = cabinetId.HasValue ? " OR run.cabinet_id = @cabinetId" : string.Empty;
        int deletedRunItems;
        await using var deleteRunItemsCmd = new NpgsqlCommand($@"
            DELETE FROM inventory_run_items item
            USING inventory_runs run
            WHERE item.inventory_run_id = run.id
              AND (run.qr_code_id = @qrCodeId{cabinetRunFilter})
        ", conn, transaction);
        
        deleteRunItemsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        if (cabinetId.HasValue)
            deleteRunItemsCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
        deletedRunItems = await deleteRunItemsCmd.ExecuteNonQueryAsync();

        // 3. Удаляем сами записи инвентаризации (inventory_runs)
        var cabinetDeleteFilter = cabinetId.HasValue ? " OR cabinet_id = @cabinetId" : string.Empty;
        int deletedRuns;
        await using var deleteRunsCmd = new NpgsqlCommand(
            $"DELETE FROM inventory_runs WHERE qr_code_id = @qrCodeId{cabinetDeleteFilter}", conn, transaction);
        
        deleteRunsCmd.Parameters.AddWithValue("qrCode aId", qrCodeId); // Исправлена опечатка оригинала: qrCodeId
        deleteRunsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        if (cabinetId.HasValue)
            deleteRunsCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
        deletedRuns = await deleteRunsCmd.ExecuteNonQueryAsync();

        int deletedChecklistItems = 0;
        int deletedPhotos = 0;
        int deletedEvents = 0;

        // 4. Если был шкаф, удаляем его внутренности
        if (cabinetId.HasValue)
        {
            await using var deleteChecklistCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_checklist_items WHERE cabinet_id = @cabinetId", conn, transaction);
            deleteChecklistCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deletedChecklistItems = await deleteChecklistCmd.ExecuteNonQueryAsync();

            await using var deletePhotosCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_photos WHERE cabinet_id = @cabinetId", conn, transaction);
            deletePhotosCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deletedPhotos = await deletePhotosCmd.ExecuteNonQueryAsync();

            await using var deleteEventsCmd = new NpgsqlCommand(
                "DELETE FROM inventory_sessions WHERE qr_code_id = @qrCodeId", conn, transaction);
            deleteEventsCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
            deletedEvents = await deleteEventsCmd.ExecuteNonQueryAsync();

            await using var deleteCabinetCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_profiles WHERE id = @cabinetId", conn, transaction);
            deleteCabinetCmd.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            await deleteCabinetCmd.ExecuteNonQueryAsync();
        }

        // 5. Коммитим транзакцию (сам QR-код удаляется в эндпоинте после этого вызова)
        await transaction.CommitAsync();

        return (cabinetId, deletedRunItems, deletedRuns, deletedChecklistItems, deletedPhotos, deletedEvents);
    }
}
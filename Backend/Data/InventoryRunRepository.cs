using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Backend.Data;

/// <summary>
/// Слой доступа к таблицам инвентаризаций:
/// - inventory_runs (записи о проверках)
/// - inventory_run_items (отметки по чек-листу)
/// - inventory_run_media (фото и голос)
/// - inventory_run_photos (дополнительные фото)
/// 
/// Содержит как методы чтения (для админки), так и транзакционные методы записи
/// (принимают готовое соединение и транзакцию), которые использует InventorySyncService.
/// </summary>
public class InventoryRunRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public InventoryRunRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    // ==========================================
    // Методы чтения (открывают соединение сами)
    // ==========================================

    /// <summary>
    /// Список последних инвентаризаций для админки (GET /api/admin/inventory/runs).
    /// </summary>
    public async Task<List<object>> GetRunsAsync(int limit)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT r.id, r.completed_at, u.full_name, u.department,
                   c.title, q.code,
                   COUNT(i.id) FILTER (WHERE i.status = 'present') AS present_count,
                   COUNT(i.id) FILTER (WHERE i.status = 'missing') AS missing_count
            FROM inventory_runs r
            JOIN users u ON u.id = r.user_id
            JOIN cabinet_profiles c ON c.id = r.cabinet_id
            JOIN qr_codes q ON q.id = r.qr_code_id
            LEFT JOIN inventory_run_items i ON i.inventory_run_id = r.id
            GROUP BY r.id, u.full_name, u.department, c.title, q.code
            ORDER BY r.completed_at DESC
            LIMIT @limit", conn);
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));

        var runs = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            runs.Add(new
            {
                id = reader.GetInt64(0),
                completedAt = reader.GetDateTime(1),
                userFullName = reader.GetString(2),
                userDepartment = reader.GetString(3),
                cabinetTitle = reader.GetString(4),
                qrCode = reader.GetString(5),
                presentCount = reader.GetInt64(6),
                missingCount = reader.GetInt64(7)
            });
        }
        return runs;
    }

    /// <summary>
    /// Детальная информация по одной инвентаризации (GET /api/admin/inventory/runs/{runId}).
    /// Возвращает null, если не найдено.
    /// </summary>
    public async Task<object?> GetRunDetailsAsync(long runId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        
        // 1. Основная информация
        long id; Guid clientRunId; DateTime startedAt, completedAt; long userId;
        string userFullName, userDepartment, cabinetTitle, qrCode; bool hasMedia; int photoCount;

        await using var mainCmd = new NpgsqlCommand(@"
            SELECT r.id, r.client_run_id, r.started_at, r.completed_at,
                   r.user_id, u.full_name, u.department, c.title, q.code,
                   EXISTS (SELECT 1 FROM inventory_run_media m WHERE m.inventory_run_id = r.id)
            FROM inventory_runs r
            JOIN users u ON u.id = r.user_id
            JOIN cabinet_profiles c ON c.id = r.cabinet_id
            JOIN qr_codes q ON q.id = r.qr_code_id
            WHERE r.id = @runId", conn);
        mainCmd.Parameters.AddWithValue("runId", runId);

        await using var reader = await mainCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        id = reader.GetInt64(0); clientRunId = reader.GetGuid(1);
        startedAt = reader.GetDateTime(2); completedAt = reader.GetDateTime(3);
        userId = reader.GetInt64(4); userFullName = reader.GetString(5);
        userDepartment = reader.GetString(6); cabinetTitle = reader.GetString(7);
        qrCode = reader.GetString(8); hasMedia = reader.GetBoolean(9);
        await reader.CloseAsync();

        // 2. Подсчет фото (основное + дополнительные)
        await using var countCmd = new NpgsqlCommand(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM inventory_run_media WHERE inventory_run_id=@id) THEN 1 ELSE 0 END 
                   + (SELECT COUNT(*) FROM inventory_run_photos WHERE inventory_run_id=@id)", conn);
        countCmd.Parameters.AddWithValue("id", runId);
        photoCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 3. Предметы чек-листа
        var items = new List<object>();
        await using var itemsCmd = new NpgsqlCommand(@"
            SELECT runItem.item_name_snapshot, runItem.status, COALESCE(runItem.comment, '')
            FROM inventory_run_items runItem
            JOIN cabinet_checklist_items checklistItem ON checklistItem.id = runItem.checklist_item_id
            WHERE runItem.inventory_run_id = @runId
            ORDER BY checklistItem.sort_order, checklistItem.id, runItem.id", conn);
        itemsCmd.Parameters.AddWithValue("runId", runId);
        await using var itemsReader = await itemsCmd.ExecuteReaderAsync();
        while (await itemsReader.ReadAsync())
        {
            items.Add(new {
                itemName = itemsReader.GetString(0),
                status = itemsReader.GetString(1),
                comment = itemsReader.GetString(2)
            });
        }

        return new {
            id, clientRunId, startedAt, completedAt, userId, userFullName,
            userDepartment, cabinetTitle, qrCode, items,
            photoUrl = hasMedia ? $"/api/admin/inventory/runs/{id}/media/photo" : null,
            photoUrls = Enumerable.Range(0, photoCount).Select(index => $"/api/admin/inventory/runs/{id}/media/photo/{index}").ToArray(),
            voiceUrl = hasMedia ? $"/api/admin/inventory/runs/{id}/media/voice" : null
        };
    }

    /// <summary>
    /// Получение байт фотографии отчета (GET .../media/photo/{index}).
    /// index=0 — основное фото, index>0 — дополнительные.
    /// </summary>
    public async Task<byte[]?> GetRunPhotoAsync(long runId, int index)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = index == 0
            ? new NpgsqlCommand("SELECT photo_jpeg FROM inventory_run_media WHERE inventory_run_id=@id", conn)
            : new NpgsqlCommand("SELECT photo_jpeg FROM inventory_run_photos WHERE inventory_run_id=@id AND photo_index=@index", conn);
        
        cmd.Parameters.AddWithValue("id", runId);
        if (index > 0) cmd.Parameters.AddWithValue("index", index);
        
        return await cmd.ExecuteScalarAsync() as byte[];
    }

    /// <summary>
    /// Получение байт медиа (фото или голос) по типу (GET .../media/{kind}).
    /// </summary>
    public async Task<byte[]?> GetRunMediaAsync(long runId, string kind)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        var sql = kind == "photo" 
            ? "SELECT photo_jpeg FROM inventory_run_media WHERE inventory_run_id = @runId"
            : "SELECT voice_wav FROM inventory_run_media WHERE inventory_run_id = @runId";
        
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("runId", runId);
        return await cmd.ExecuteScalarAsync() as byte[];
    }

    // ==========================================
    // Транзакционные методы (принимают conn + tran)
    // Используются внутри InventorySyncService.SaveRunAsync
    // ==========================================

    public async Task<bool> ExistsByClientRunIdAsync(NpgsqlConnection conn, NpgsqlTransaction tran, Guid clientRunId)
    {
        await using var cmd = new NpgsqlCommand("SELECT id FROM inventory_runs WHERE client_run_id = @clientRunId", conn, tran);
        cmd.Parameters.AddWithValue("clientRunId", clientRunId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    public async Task<long> CreateRunAsync(NpgsqlConnection conn, NpgsqlTransaction tran,
        Guid clientRunId, long cabinetId, int userId, long qrCodeId, DateTime startedAt, DateTime completedAt)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_runs (client_run_id, cabinet_id, user_id, qr_code_id, started_at, completed_at)
            VALUES (@clientRunId, @cabinetId, @userId, @qrCodeId, @startedAt, @completedAt)
            ON CONFLICT (client_run_id) DO NOTHING
            RETURNING id", conn, tran);
        
        cmd.Parameters.AddWithValue("clientRunId", clientRunId);
        cmd.Parameters.AddWithValue("cabinetId", cabinetId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        cmd.Parameters.AddWithValue("startedAt", startedAt);
        cmd.Parameters.AddWithValue("completedAt", completedAt);

        var result = await cmd.ExecuteScalarAsync();
        return result is long id ? id : -1; // -1 означает дубликат (DO NOTHING)
    }

    public async Task InsertRunMediaAsync(NpgsqlConnection conn, NpgsqlTransaction tran, long runId, byte[] photo, byte[] voice)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_run_media (inventory_run_id, photo_jpeg, voice_wav)
            VALUES (@runId, @photo, @voice)", conn, tran);
        
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("photo", NpgsqlDbType.Bytea, photo);
        cmd.Parameters.AddWithValue("voice", NpgsqlDbType.Bytea, voice);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertRunPhotoAsync(NpgsqlConnection conn, NpgsqlTransaction tran, long runId, int photoIndex, byte[] photo)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_run_photos (inventory_run_id, photo_index, photo_jpeg)
            VALUES (@runId, @photoIndex, @photo)", conn, tran);
        
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("photoIndex", photoIndex);
        cmd.Parameters.AddWithValue("photo", NpgsqlDbType.Bytea, photo);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertRunItemAsync(NpgsqlConnection conn, NpgsqlTransaction tran,
        long runId, long itemId, string itemName, string status, string? comment)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_run_items (inventory_run_id, checklist_item_id, item_name_snapshot, status, comment)
            VALUES (@runId, @itemId, @itemName, @status, @comment)", conn, tran);
        
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("itemName", itemName);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("comment", string.IsNullOrWhiteSpace(comment) ? (object)DBNull.Value : comment.Trim());
        await cmd.ExecuteNonQueryAsync();
    }
}
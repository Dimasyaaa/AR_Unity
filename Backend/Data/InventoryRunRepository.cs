using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Backend.Data;

/// <summary>Вложение отчёта вместе с MIME-типом, сохранённым в базе.</summary>
public sealed record InventoryRunAttachment(byte[] Data, string ContentType);
public sealed record InventoryBatchExportRow(
    string BoxTitle, string QrCode, string ItemName,
    int ExpectedQuantity, int ActualQuantity, string Status, string Comment,
    string UserFullName, string Department, DateTime CompletedAt);

/// <summary>
/// Слой доступа к таблицам инвентаризаций:
/// - inventory_runs (записи о проверках)
/// - inventory_run_items (отметки по чек-листу)
/// - inventory_run_attachments (фотографии и необязательная голосовая запись)
/// 
/// Содержит как методы чтения (для админки), так и транзакционные методы записи
/// (принимают готовое соединение и транзакцию), которые использует InventorySyncService.
/// </summary>
public class InventoryRunRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public InventoryRunRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Создаёт серверное задание на проверку выбранного списка коробок.</summary>
    public async Task<(long id, Guid clientBatchId)> CreateAssignmentAsync(
        long userId, IReadOnlyList<long> requestedQrIds)
    {
        long[] qrIds = requestedQrIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<long>();
        if (qrIds.Length == 0)
            throw new ArgumentException("Выберите хотя бы одну коробку для инвентаризации.");

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tran = await conn.BeginTransactionAsync();

        await using (var userCommand = new NpgsqlCommand(
            "SELECT 1 FROM users WHERE id = @userId AND is_active = TRUE", conn, tran))
        {
            userCommand.Parameters.AddWithValue("userId", userId);
            if (await userCommand.ExecuteScalarAsync() is null)
                throw new ArgumentException("Выбранный сотрудник не найден или отключён.");
        }

        await using (var activeCommand = new NpgsqlCommand(@"
            SELECT 1 FROM inventory_batches
            WHERE user_id = @userId AND status IN ('assigned', 'in_progress')
            LIMIT 1", conn, tran))
        {
            activeCommand.Parameters.AddWithValue("userId", userId);
            if (await activeCommand.ExecuteScalarAsync() is not null)
                throw new InvalidOperationException("У этого сотрудника уже есть незавершённое назначение.");
        }

        await using (var qrCommand = new NpgsqlCommand(@"
            SELECT q.id
            FROM qr_codes q
            JOIN boxes box ON box.qr_code_id = q.id AND box.is_active = TRUE
            WHERE q.id = ANY(@qrIds) AND q.is_active = TRUE
              AND EXISTS (
                  SELECT 1 FROM box_items item
                  WHERE item.box_id = box.id AND item.is_active = TRUE
              )", conn, tran))
        {
            qrCommand.Parameters.AddWithValue("qrIds", NpgsqlDbType.Array | NpgsqlDbType.Bigint, qrIds);
            var validIds = new List<long>();
            await using var reader = await qrCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync()) validIds.Add(reader.GetInt64(0));
            if (validIds.Count != qrIds.Length)
                throw new ArgumentException("Каждый QR должен принадлежать активной коробке с заполненным перечнем.");
        }

        Guid clientBatchId = Guid.NewGuid();
        long batchId;
        await using (var batchCommand = new NpgsqlCommand(@"
            INSERT INTO inventory_batches
                (client_batch_id, user_id, expected_box_count, status, started_at)
            VALUES (@clientBatchId, @userId, @boxCount, 'assigned', NOW())
            RETURNING id", conn, tran))
        {
            batchCommand.Parameters.AddWithValue("clientBatchId", clientBatchId);
            batchCommand.Parameters.AddWithValue("userId", userId);
            batchCommand.Parameters.AddWithValue("boxCount", qrIds.Length);
            batchId = (long)(await batchCommand.ExecuteScalarAsync())!;
        }

        foreach (long qrId in qrIds)
        {
            await using var boxCommand = new NpgsqlCommand(@"
                INSERT INTO inventory_batch_boxes (inventory_batch_id, qr_code_id)
                VALUES (@batchId, @qrCodeId)", conn, tran);
            boxCommand.Parameters.AddWithValue("batchId", batchId);
            boxCommand.Parameters.AddWithValue("qrCodeId", qrId);
            await boxCommand.ExecuteNonQueryAsync();
        }

        await tran.CommitAsync();
        return (batchId, clientBatchId);
    }

    /// <summary>Возвращает назначение сотрудника вместе со списком разрешённых QR-кодов.</summary>
    public async Task<InventoryAssignmentDto?> GetActiveAssignmentForUserAsync(long userId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        long batchId;
        var assignment = new InventoryAssignmentDto();

        await using (var batchCommand = new NpgsqlCommand(@"
            SELECT batch.id, batch.client_batch_id, batch.expected_box_count, batch.status
            FROM inventory_batches batch
            WHERE batch.user_id = @userId AND batch.status IN ('assigned', 'in_progress')
              AND EXISTS (
                  SELECT 1 FROM inventory_batch_boxes assigned
                  WHERE assigned.inventory_batch_id = batch.id
              )
            ORDER BY batch.created_at
            LIMIT 1", conn))
        {
            batchCommand.Parameters.AddWithValue("userId", userId);
            await using var reader = await batchCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            batchId = reader.GetInt64(0);
            assignment.clientBatchId = reader.GetGuid(1).ToString("D");
            assignment.expectedBoxCount = reader.GetInt32(2);
            assignment.status = reader.GetString(3);
        }

        await using var boxesCommand = new NpgsqlCommand(@"
            SELECT qr.id, qr.code, COALESCE(box.title, qr.object_name)
            FROM inventory_batch_boxes assigned
            JOIN qr_codes qr ON qr.id = assigned.qr_code_id
            LEFT JOIN boxes box ON box.qr_code_id = qr.id
            WHERE assigned.inventory_batch_id = @batchId
            ORDER BY qr.code", conn);
        boxesCommand.Parameters.AddWithValue("batchId", batchId);
        await using var boxesReader = await boxesCommand.ExecuteReaderAsync();
        while (await boxesReader.ReadAsync())
        {
            assignment.boxes.Add(new AssignedBoxDto
            {
                qrCodeId = boxesReader.GetInt64(0),
                qrCode = boxesReader.GetString(1),
                title = boxesReader.GetString(2)
            });
        }

        return assignment;
    }

    /// <summary>Возвращает активное назначение и последние завершённые общие проверки сотрудника.</summary>
    public async Task<InventoryAssignmentOverviewDto> GetAssignmentOverviewForUserAsync(long userId)
    {
        var overview = new InventoryAssignmentOverviewDto
        {
            activeAssignment = await GetActiveAssignmentForUserAsync(userId)
        };

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT batch.client_batch_id, batch.expected_box_count, batch.status,
                   batch.started_at, batch.completed_at, COUNT(run.id)::INT
            FROM inventory_batches batch
            LEFT JOIN inventory_runs run ON run.batch_id = batch.id
            WHERE batch.user_id = @userId AND batch.status = 'completed'
            GROUP BY batch.id
            ORDER BY batch.completed_at DESC NULLS LAST, batch.started_at DESC
            LIMIT 20", conn);
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateTime started = reader.GetDateTime(3);
            overview.history.Add(new InventoryBatchHistoryDto
            {
                clientBatchId = reader.GetGuid(0).ToString("D"),
                expectedBoxCount = reader.GetInt32(1),
                status = reader.GetString(2),
                startedAt = new DateTimeOffset(DateTime.SpecifyKind(started, DateTimeKind.Utc)),
                completedAt = reader.IsDBNull(4)
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)),
                completedBoxCount = reader.GetInt32(5)
            });
        }

        return overview;
    }

    /// <summary>Проверяет QR в назначении; старые локальные запуски без списка остаются совместимыми.</summary>
    public async Task<bool> IsQrAllowedInBatchAsync(
        NpgsqlConnection conn, NpgsqlTransaction tran, long batchId, long qrCodeId)
    {
        await using var cmd = new NpgsqlCommand(@"
            SELECT NOT EXISTS (
                       SELECT 1 FROM inventory_batch_boxes WHERE inventory_batch_id = @batchId
                   ) OR EXISTS (
                       SELECT 1 FROM inventory_batch_boxes
                       WHERE inventory_batch_id = @batchId AND qr_code_id = @qrCodeId
                   )", conn, tran);
        cmd.Parameters.AddWithValue("batchId", batchId);
        cmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

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
                   COUNT(i.id) FILTER (WHERE i.status = 'partial') AS partial_count,
                   COUNT(i.id) FILTER (WHERE i.status = 'missing') AS missing_count
            FROM inventory_runs r
            JOIN users u ON u.id = r.user_id
            JOIN boxes c ON c.id = r.box_id
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
                boxTitle = reader.GetString(4),
                qrCode = reader.GetString(5),
                presentCount = reader.GetInt64(6),
                partialCount = reader.GetInt64(7),
                missingCount = reader.GetInt64(8)
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
        string userFullName, userDepartment, boxTitle, qrCode;
        string? reportNote;
        bool hasPhotos, hasVoice;

        await using var mainCmd = new NpgsqlCommand(@"
            SELECT r.id, r.client_run_id, r.started_at, r.completed_at,
                   r.user_id, u.full_name, u.department, c.title, q.code, r.report_note,
                   EXISTS (
                       SELECT 1 FROM inventory_run_attachments a
                       WHERE a.inventory_run_id = r.id AND a.attachment_type = 'photo'
                   ),
                   EXISTS (
                       SELECT 1 FROM inventory_run_attachments a
                       WHERE a.inventory_run_id = r.id AND a.attachment_type = 'voice'
                   )
            FROM inventory_runs r
            JOIN users u ON u.id = r.user_id
            JOIN boxes c ON c.id = r.box_id
            JOIN qr_codes q ON q.id = r.qr_code_id
            WHERE r.id = @runId", conn);
        mainCmd.Parameters.AddWithValue("runId", runId);

        await using var reader = await mainCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        id = reader.GetInt64(0); clientRunId = reader.GetGuid(1);
        startedAt = reader.GetDateTime(2); completedAt = reader.GetDateTime(3);
        userId = reader.GetInt64(4); userFullName = reader.GetString(5);
        userDepartment = reader.GetString(6); boxTitle = reader.GetString(7);
        qrCode = reader.GetString(8);
        reportNote = reader.IsDBNull(9) ? null : reader.GetString(9);
        hasPhotos = reader.GetBoolean(10);
        hasVoice = reader.GetBoolean(11);
        await reader.CloseAsync();

        // 2. Читаем реальные индексы фото: они могут отсутствовать или идти не подряд.
        var photoIndices = new List<int>();
        await using (var photoIndexCmd = new NpgsqlCommand(@"
            SELECT attachment_index
            FROM inventory_run_attachments
            WHERE inventory_run_id = @id AND attachment_type = 'photo'
            ORDER BY attachment_index", conn))
        {
            photoIndexCmd.Parameters.AddWithValue("id", runId);
            await using var photoIndexReader = await photoIndexCmd.ExecuteReaderAsync();
            while (await photoIndexReader.ReadAsync())
                photoIndices.Add(photoIndexReader.GetInt32(0));
        }

        // 3. Предметы чек-листа
        var items = new List<object>();
        await using var itemsCmd = new NpgsqlCommand(@"
            SELECT runItem.item_name_snapshot, runItem.status, COALESCE(runItem.comment, ''),
                   runItem.expected_quantity_snapshot, runItem.actual_quantity
            FROM inventory_run_items runItem
            JOIN box_items checklistItem ON checklistItem.id = runItem.box_item_id
            WHERE runItem.inventory_run_id = @runId
            ORDER BY checklistItem.sort_order, checklistItem.id, runItem.id", conn);
        itemsCmd.Parameters.AddWithValue("runId", runId);
        await using var itemsReader = await itemsCmd.ExecuteReaderAsync();
        while (await itemsReader.ReadAsync())
        {
            items.Add(new {
                itemName = itemsReader.GetString(0),
                status = itemsReader.GetString(1),
                comment = itemsReader.GetString(2),
                expectedQuantity = itemsReader.GetInt32(3),
                actualQuantity = itemsReader.GetInt32(4)
            });
        }

        return new {
            id, clientRunId, startedAt, completedAt, userId, userFullName,
            userDepartment, boxTitle, qrCode, items, reportNote,
            photoUrl = hasPhotos ? $"/api/admin/inventory/runs/{id}/media/photo" : null,
            photoUrls = photoIndices.Select(index => $"/api/admin/inventory/runs/{id}/media/photo/{index}").ToArray(),
            voiceUrl = hasVoice ? $"/api/admin/inventory/runs/{id}/media/voice" : null
        };
    }

    /// <summary>
    /// Получение байт фотографии отчета (GET .../media/photo/{index}).
    /// index=0 — основное фото, index>0 — дополнительные.
    /// </summary>
    public async Task<InventoryRunAttachment?> GetRunPhotoAsync(long runId, int index)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT data, content_type
            FROM inventory_run_attachments
            WHERE inventory_run_id = @id
              AND attachment_type = 'photo'
              AND attachment_index = @index", conn);
        cmd.Parameters.AddWithValue("id", runId);
        cmd.Parameters.AddWithValue("index", index);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new InventoryRunAttachment(reader.GetFieldValue<byte[]>(0), reader.GetString(1))
            : null;
    }

    /// <summary>
    /// Получение байт медиа (фото или голос) по типу (GET .../media/{kind}).
    /// </summary>
    public async Task<InventoryRunAttachment?> GetRunMediaAsync(long runId, string kind)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        if (kind is not ("photo" or "voice"))
            return null;

        await using var cmd = new NpgsqlCommand(@"
            SELECT data, content_type
            FROM inventory_run_attachments
            WHERE inventory_run_id = @runId
              AND attachment_type = @kind
              AND attachment_index = 0", conn);
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("kind", kind);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new InventoryRunAttachment(reader.GetFieldValue<byte[]>(0), reader.GetString(1))
            : null;
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

    /// <summary>Создаёт общую проверку либо возвращает уже созданную для офлайн-пакета.</summary>
    public async Task<long?> GetOrCreateBatchIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tran, Guid clientBatchId,
        int userId, int expectedBoxCount, DateTime startedAt)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_batches (client_batch_id, user_id, expected_box_count, started_at)
            VALUES (@clientBatchId, @userId, @expectedBoxCount, @startedAt)
            ON CONFLICT (client_batch_id) DO UPDATE
                SET status = 'in_progress', updated_at = NOW()
                WHERE inventory_batches.user_id = EXCLUDED.user_id
                  AND inventory_batches.expected_box_count = EXCLUDED.expected_box_count
                  AND inventory_batches.status IN ('assigned', 'in_progress')
            RETURNING id", conn, tran);
        cmd.Parameters.AddWithValue("clientBatchId", clientBatchId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("expectedBoxCount", expectedBoxCount);
        cmd.Parameters.AddWithValue("startedAt", startedAt);

        var result = await cmd.ExecuteScalarAsync();
        return result is long id ? id : null;
    }

    /// <summary>Не позволяет повторно проверять ту же коробку в рамках одной общей проверки.</summary>
    public async Task<bool> BatchAlreadyContainsQrAsync(
        NpgsqlConnection conn, NpgsqlTransaction tran, long batchId, long qrCodeId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM inventory_runs WHERE batch_id = @batchId AND qr_code_id = @qrCodeId)",
            conn, tran);
        cmd.Parameters.AddWithValue("batchId", batchId);
        cmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    /// <summary>Завершает общую проверку, как только записаны все ожидаемые коробки.</summary>
    public async Task<bool> CompleteBatchIfReadyAsync(
        NpgsqlConnection conn, NpgsqlTransaction tran, long batchId, DateTime completedAt)
    {
        await using var cmd = new NpgsqlCommand(@"
            UPDATE inventory_batches batch
            SET status = 'completed', completed_at = @completedAt, updated_at = NOW()
            WHERE batch.id = @batchId
              AND batch.status = 'in_progress'
              AND (SELECT COUNT(*) FROM inventory_runs run WHERE run.batch_id = batch.id)
                    >= batch.expected_box_count
            RETURNING id", conn, tran);
        cmd.Parameters.AddWithValue("batchId", batchId);
        cmd.Parameters.AddWithValue("completedAt", completedAt);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    public async Task<long> CreateRunAsync(NpgsqlConnection conn, NpgsqlTransaction tran,
        Guid clientRunId, long boxId, int userId, long qrCodeId, DateTime startedAt, DateTime completedAt,
        long? batchId, string? reportNote)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_runs
                (client_run_id, box_id, user_id, qr_code_id, started_at, completed_at, batch_id, report_note)
            VALUES (@clientRunId, @boxId, @userId, @qrCodeId, @startedAt, @completedAt, @batchId, @reportNote)
            ON CONFLICT (client_run_id) DO NOTHING
            RETURNING id", conn, tran);
        
        cmd.Parameters.AddWithValue("clientRunId", clientRunId);
        cmd.Parameters.AddWithValue("boxId", boxId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        cmd.Parameters.AddWithValue("startedAt", startedAt);
        cmd.Parameters.AddWithValue("completedAt", completedAt);
        cmd.Parameters.AddWithValue("batchId", batchId is null ? DBNull.Value : batchId.Value);
        cmd.Parameters.AddWithValue("reportNote", string.IsNullOrWhiteSpace(reportNote) ? DBNull.Value : reportNote.Trim());

        var result = await cmd.ExecuteScalarAsync();
        return result is long id ? id : -1; // -1 означает дубликат (DO NOTHING)
    }

    public async Task InsertRunAttachmentAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tran,
        long runId,
        string attachmentType,
        int attachmentIndex,
        string contentType,
        byte[] data)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_run_attachments
                (inventory_run_id, attachment_type, attachment_index, content_type, data)
            VALUES (@runId, @type, @index, @contentType, @data)", conn, tran);
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("type", attachmentType);
        cmd.Parameters.AddWithValue("index", attachmentIndex);
        cmd.Parameters.AddWithValue("contentType", contentType);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Bytea, data);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertRunItemAsync(NpgsqlConnection conn, NpgsqlTransaction tran,
        long runId, long itemId, string itemName, string status, int expectedQuantity,
        int actualQuantity, string? comment)
    {
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_run_items
                (inventory_run_id, box_item_id, item_name_snapshot, status,
                 expected_quantity_snapshot, actual_quantity, comment)
            VALUES (@runId, @itemId, @itemName, @status, @expectedQuantity, @actualQuantity, @comment)", conn, tran);
        
        cmd.Parameters.AddWithValue("runId", runId);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("itemName", itemName);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("expectedQuantity", expectedQuantity);
        cmd.Parameters.AddWithValue("actualQuantity", actualQuantity);
        cmd.Parameters.AddWithValue("comment", string.IsNullOrWhiteSpace(comment) ? (object)DBNull.Value : comment.Trim());
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Возвращает строки общей инвентаризации в плоском виде для таблицы и Excel.</summary>
    public async Task<List<InventoryBatchExportRow>> GetBatchExportRowsAsync(long batchId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT box.title, qr.code, result.item_name_snapshot,
                   result.expected_quantity_snapshot, result.actual_quantity, result.status,
                   COALESCE(result.comment, ''), userRow.full_name, userRow.department,
                   run.completed_at
            FROM inventory_runs run
            JOIN inventory_run_items result ON result.inventory_run_id = run.id
            JOIN boxes box ON box.id = run.box_id
            JOIN qr_codes qr ON qr.id = run.qr_code_id
            JOIN users userRow ON userRow.id = run.user_id
            WHERE run.batch_id = @batchId
            ORDER BY qr.code, result.item_name_snapshot", conn);
        cmd.Parameters.AddWithValue("batchId", batchId);

        var rows = new List<InventoryBatchExportRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new InventoryBatchExportRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetDateTime(9)));
        }
        return rows;
    }

    /// <summary>Список общих проверок для администраторской панели.</summary>
    public async Task<List<object>> GetBatchesAsync(int limit)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT batch.id, batch.expected_box_count, batch.status, batch.started_at,
                   batch.completed_at, userRow.full_name, userRow.department,
                   COUNT(DISTINCT run.id) AS boxes_completed,
                   COUNT(DISTINCT run_item.id) AS positions_count
            FROM inventory_batches batch
            JOIN users userRow ON userRow.id = batch.user_id
            LEFT JOIN inventory_runs run ON run.batch_id = batch.id
            LEFT JOIN inventory_run_items run_item ON run_item.inventory_run_id = run.id
            GROUP BY batch.id, userRow.full_name, userRow.department
            ORDER BY batch.started_at DESC
            LIMIT @limit", conn);
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));

        var batches = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            batches.Add(new
            {
                id = reader.GetInt64(0),
                expectedBoxCount = reader.GetInt32(1),
                status = reader.GetString(2),
                startedAt = reader.GetDateTime(3),
                completedAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                userFullName = reader.GetString(5),
                userDepartment = reader.GetString(6),
                completedBoxCount = reader.GetInt64(7),
                positionCount = reader.GetInt64(8),
                excelUrl = $"/api/admin/inventory/batches/{reader.GetInt64(0)}/excel"
            });
        }
        return batches;
    }
}

using Backend.Helpers;
using Npgsql;
using NpgsqlTypes;

namespace Backend.Services;

/// <summary>Импортирует опись в общую инвентаризацию и связывает каждую строку с QR-кодом.</summary>
public sealed class InventoryImportService
{
    private const int MaximumWorkbookSize = 20 * 1024 * 1024;
    private const long InventoryLockId = 746231885412501;
    private readonly NpgsqlDataSource _dataSource;

    public InventoryImportService(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Читает книгу и возвращает строки для предварительного просмотра в админке.</summary>
    public List<InventoryOpisRow> Preview(byte[] workbookBytes) => InventoryOpisExcelReader.Read(workbookBytes);

    /// <summary>Создаёт общую опись, QR-коды и позиции без назначения конкретному сотруднику.</summary>
    public async Task<(long batchId, Guid clientBatchId, int positionCount)> StartAsync(
        byte[] workbookBytes,
        string fileName)
    {
        if (workbookBytes.Length == 0 || workbookBytes.Length > MaximumWorkbookSize)
            throw new InvalidDataException("Размер Excel-файла должен быть от 1 байта до 20 МБ.");

        List<InventoryOpisRow> rows = InventoryOpisExcelReader.Read(workbookBytes);
        ValidateRows(rows);
        var payloads = rows.Select(row => QrHelper.CreateInventoryPayload(row.EnsCode, row.AccountingQuantity!.Value)).ToList();
        string? duplicatePayload = payloads.GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicatePayload is not null)
            throw new InvalidDataException($"В описи повторяется QR-содержимое «{duplicatePayload}». Каждая строка МТР должна иметь уникальный QR.");

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // Блокировка не даёт двум одновременным запросам запустить две активные описи.
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@lockId)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("lockId", InventoryLockId);
            await lockCommand.ExecuteNonQueryAsync();
        }

        await using (var activeCommand = new NpgsqlCommand(@"
            SELECT id
            FROM inventory_batches
            WHERE source_file_data IS NOT NULL AND status IN ('assigned', 'in_progress')
            ORDER BY started_at DESC
            LIMIT 1", connection, transaction))
        {
            object? activeId = await activeCommand.ExecuteScalarAsync();
            if (activeId is not null)
                throw new InvalidOperationException("Уже есть активная опись. Завершите текущую инвентаризацию перед загрузкой следующей.");
        }

        Guid clientBatchId = Guid.NewGuid();
        string safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "Инвентаризационная_опись.xlsx";

        long batchId;
        await using (var batchCommand = new NpgsqlCommand(@"
            INSERT INTO inventory_batches
                (client_batch_id, user_id, expected_box_count, status, started_at,
                 inventory_title, source_file_name, source_file_data)
            VALUES (@clientBatchId, NULL, @positionCount, 'assigned', NOW(),
                    @title, @fileName, @fileData)
            RETURNING id", connection, transaction))
        {
            batchCommand.Parameters.AddWithValue("clientBatchId", clientBatchId);
            batchCommand.Parameters.AddWithValue("positionCount", rows.Count);
            batchCommand.Parameters.AddWithValue("title", Path.GetFileNameWithoutExtension(safeFileName));
            batchCommand.Parameters.AddWithValue("fileName", safeFileName);
            batchCommand.Parameters.Add("fileData", NpgsqlDbType.Bytea).Value = workbookBytes;
            batchId = (long)(await batchCommand.ExecuteScalarAsync())!;
        }

        for (int index = 0; index < rows.Count; index++)
        {
            InventoryOpisRow row = rows[index];
            string qrPayload = payloads[index];
            long qrCodeId = await UpsertQrAsync(connection, transaction, qrPayload, row.ItemName);
            long boxId = await UpsertBoxAsync(connection, transaction, qrCodeId, row.ItemName);
            await UpsertBoxItemAsync(connection, transaction, boxId, row);
            await AddBatchQrAsync(connection, transaction, batchId, qrCodeId, index + 1);
        }

        await transaction.CommitAsync();
        return (batchId, clientBatchId, rows.Count);
    }

    /// <summary>Возвращает метаданные единственной активной описи для главного экрана.</summary>
    public async Task<object?> GetCurrentAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT batch.id, batch.inventory_title, batch.source_file_name, batch.expected_box_count,
                   batch.status, batch.started_at, COUNT(DISTINCT run.id)::INT AS completed_count
            FROM inventory_batches batch
            LEFT JOIN inventory_runs run ON run.batch_id = batch.id
            WHERE batch.source_file_data IS NOT NULL AND batch.status IN ('assigned', 'in_progress')
            GROUP BY batch.id
            ORDER BY batch.started_at DESC
            LIMIT 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new
        {
            id = reader.GetInt64(0),
            title = reader.GetString(1),
            fileName = reader.GetString(2),
            expectedPositionCount = reader.GetInt32(3),
            status = reader.GetString(4),
            startedAt = reader.GetDateTime(5),
            completedPositionCount = reader.GetInt32(6)
        };
    }

    /// <summary>Скачивает исходную книгу, чтобы итоговый экспорт сохранил её оформление.</summary>
    public async Task<(byte[] workbook, string fileName)?> GetSourceWorkbookAsync(long batchId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT source_file_data, source_file_name
            FROM inventory_batches
            WHERE id = @batchId AND source_file_data IS NOT NULL", connection);
        command.Parameters.AddWithValue("batchId", batchId);
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync()) return null;
        return ((byte[])reader[0], reader.GetString(1));
    }

    /// <summary>Создаёт или повторно активирует QR для стабильной пары ЕНС и учётного количества.</summary>
    private static async Task<long> UpsertQrAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string payload, string itemName)
    {
        await using var find = new NpgsqlCommand(
            "SELECT id FROM qr_codes WHERE code = @code FOR UPDATE", connection, transaction);
        find.Parameters.AddWithValue("code", payload);
        object? existing = await find.ExecuteScalarAsync();
        if (existing is long existingId)
        {
            await using var update = new NpgsqlCommand(@"
                UPDATE qr_codes SET object_name = @name, is_active = TRUE WHERE id = @id", connection, transaction);
            update.Parameters.AddWithValue("id", existingId);
            update.Parameters.AddWithValue("name", itemName);
            await update.ExecuteNonQueryAsync();
            return existingId;
        }

        await using var insert = new NpgsqlCommand(@"
            INSERT INTO qr_codes (code, object_name, is_active)
            VALUES (@code, @name, TRUE)
            RETURNING id", connection, transaction);
        insert.Parameters.AddWithValue("code", payload);
        insert.Parameters.AddWithValue("name", itemName);
        return (long)(await insert.ExecuteScalarAsync())!;
    }

    /// <summary>Связывает QR с карточкой, используя текущую модель карточки и не трогая её вложения.</summary>
    private static async Task<long> UpsertBoxAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long qrCodeId, string itemName)
    {
        await using var command = new NpgsqlCommand(@"
            INSERT INTO boxes (qr_code_id, title, is_active)
            VALUES (@qrCodeId, @title, TRUE)
            ON CONFLICT (qr_code_id) DO UPDATE SET
                title = EXCLUDED.title, is_active = TRUE, updated_at = NOW()
            RETURNING id", connection, transaction);
        command.Parameters.AddWithValue("qrCodeId", qrCodeId);
        command.Parameters.AddWithValue("title", itemName);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Сохраняет одну строку МТР в перечне карточки, сохраняя её идентификатор для истории.</summary>
    private static async Task UpsertBoxItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long boxId, InventoryOpisRow row)
    {
        var ids = new List<long>();
        await using (var find = new NpgsqlCommand(@"
            SELECT id FROM box_items WHERE box_id = @boxId AND is_active = TRUE ORDER BY id FOR UPDATE", connection, transaction))
        {
            find.Parameters.AddWithValue("boxId", boxId);
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
        }

        if (ids.Count > 1)
            throw new InvalidOperationException($"QR «{row.EnsCode}» уже связан с несколькими позициями. Проверьте его в разделе коробок перед повторным импортом.");

        const string values = @"
            name = @name, description = '', sort_order = 10, expected_quantity = @quantity,
            model_type = 'cube', structural_division = @division, warehouse = @warehouse,
            ens_code = @ens, account_code = @account, mol = @mol,
            storage_location = @location, unit = @unit, is_active = TRUE";
        await using var command = ids.Count == 1
            ? new NpgsqlCommand($"UPDATE box_items SET {values} WHERE id = @id AND box_id = @boxId", connection, transaction)
            : new NpgsqlCommand(@"
                INSERT INTO box_items
                    (box_id, name, description, sort_order, is_active, expected_quantity, model_type,
                     structural_division, warehouse, ens_code, account_code, mol, storage_location, unit)
                VALUES (@boxId, @name, '', 10, TRUE, @quantity, 'cube',
                        @division, @warehouse, @ens, @account, @mol, @location, @unit)", connection, transaction);

        if (ids.Count == 1) command.Parameters.AddWithValue("id", ids[0]);
        command.Parameters.AddWithValue("boxId", boxId);
        command.Parameters.AddWithValue("name", row.ItemName);
        command.Parameters.AddWithValue("quantity", row.AccountingQuantity!.Value);
        AddTextParameter(command, "division", row.StructuralDivision);
        AddTextParameter(command, "warehouse", row.Warehouse);
        AddTextParameter(command, "ens", row.EnsCode);
        AddTextParameter(command, "account", row.AccountCode);
        AddTextParameter(command, "mol", row.Mol);
        AddTextParameter(command, "location", row.StorageLocation);
        AddTextParameter(command, "unit", row.Unit);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Добавляет QR в перечень общей проверки, не создавая повторную связь.</summary>
    private static async Task AddBatchQrAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long batchId, long qrCodeId, int sortOrder)
    {
        await using var command = new NpgsqlCommand(@"
            INSERT INTO inventory_batch_boxes (inventory_batch_id, qr_code_id, sort_order)
            VALUES (@batchId, @qrCodeId, @sortOrder)
            ON CONFLICT DO NOTHING", connection, transaction);
        command.Parameters.AddWithValue("batchId", batchId);
        command.Parameters.AddWithValue("qrCodeId", qrCodeId);
        command.Parameters.AddWithValue("sortOrder", sortOrder * 10);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Проверяет обязательные поля и ограничения до открытия транзакции записи.</summary>
    private static void ValidateRows(IReadOnlyList<InventoryOpisRow> rows)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            InventoryOpisRow row = rows[index];
            if (string.IsNullOrWhiteSpace(row.ItemName))
                throw new InvalidDataException($"В строке {row.SourceRowNumber} не заполнено наименование МТР.");
            if (string.IsNullOrWhiteSpace(row.EnsCode) || row.EnsCode.Length > 100 || row.EnsCode.Contains('#')
                || row.EnsCode.Contains('\r') || row.EnsCode.Contains('\n'))
                throw new InvalidDataException($"В строке {row.SourceRowNumber} некорректный ЕНС/СКК — он используется в QR.");
            if (row.AccountingQuantity is null or < 1 or > 100000)
                throw new InvalidDataException($"В строке {row.SourceRowNumber} укажите учётное количество от 1 до 100000.");
            if (new[] { row.StructuralDivision, row.Warehouse, row.AccountCode, row.Mol, row.StorageLocation, row.Unit }
                .Any(value => value.Length > 200))
                throw new InvalidDataException($"В строке {row.SourceRowNumber} одно из полей длиннее 200 символов.");
        }
    }

    /// <summary>Пишет пустые реквизиты как NULL, сохраняя SQL-тип параметра.</summary>
    private static void AddTextParameter(NpgsqlCommand command, string name, string value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }
}

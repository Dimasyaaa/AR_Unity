using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Contracts;
using Backend.Data;
using Backend.Helpers;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Бизнес-логика обработки синхронизации инвентаризаций с мобильных устройств.
/// Оркестрирует транзакцию: проверяет повторы, валидирует данные,
/// декодирует медиа через ReportMediaService и сохраняет всё в БД через InventoryRunRepository.
/// </summary>
public class InventorySyncService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly InventoryRunRepository _runRepository;

    public InventorySyncService(
        NpgsqlDataSource dataSource, 
        InventoryRunRepository runRepository)
    {
        _dataSource = dataSource;
        _runRepository = runRepository;
    }

    /// <summary>
    /// Обрабатывает пакет синхронизации (POST /api/sync/inventory-runs).
    /// Возвращает статистику: сколько импортировано, сколько дублей, сколько пропущено с ошибками.
    /// </summary>
    public async Task<(int imported, int duplicates, int skipped, List<string> errors)> ProcessInventoryRunsAsync(InventoryRunSyncPayload payload)
    {
        int imported = 0, duplicates = 0, skipped = 0;
        var errors = new List<string>();

        // Открываем одно соединение на весь пакет для эффективности
        await using var conn = await _dataSource.OpenConnectionAsync();

        foreach (var run in payload.items)
        {
            try
            {
                var result = await SaveRunAsync(conn, run);
                if (result == SaveRunResult.Imported) imported++;
                else if (result == SaveRunResult.Duplicate) duplicates++;
                else skipped++;
            }
            catch (ArgumentException ex)
            {
                skipped++;
                errors.Add(ex.Message);
            }
        }

        return (imported, duplicates, skipped, errors);
    }

    private async Task<SaveRunResult> SaveRunAsync(NpgsqlConnection conn, InventoryRunSyncItem run)
    {
        // 1. Базовая валидация входных данных
        if (run.clientRunId == Guid.Empty)
            throw new ArgumentException("Проверка пропущена: отсутствует clientRunId.");
        if (run.userId < 0 || run.userId > int.MaxValue || string.IsNullOrWhiteSpace(run.qrCode))
            throw new ArgumentException("Проверка пропущена: некорректный пользователь или QR.");
        if (run.items == null || run.items.Count == 0)
            throw new ArgumentException("Проверка пропущена: список предметов пуст.");

        var duplicateItem = run.items.GroupBy(item => item.itemId).FirstOrDefault(group => group.Count() > 1);
        if (duplicateItem != null)
            throw new ArgumentException("Проверка пропущена: предмет повторяется в списке.");
        if (run.items.Any(item => item.itemId <= 0 ||
            (item.actualQuantity is null && item.status is not ("present" or "missing")) ||
            item.actualQuantity is < 0 or > 100000))
            throw new ArgumentException("Проверка пропущена: укажите «Есть» или «Нет» и допустимое количество предмета.");
        if (run.clientBatchId == Guid.Empty)
            throw new ArgumentException("Проверка пропущена: идентификатор общей инвентаризации некорректен.");
        if (run.clientBatchId.HasValue && (run.expectedBoxCount is null or < 1))
            throw new ArgumentException("Проверка пропущена: количество коробок должно быть не меньше одного.");
        string? reportNote = string.IsNullOrWhiteSpace(run.reportNote) ? null : run.reportNote.Trim();
        if (reportNote?.Length > 2000)
            throw new ArgumentException("Текстовая заметка не должна превышать 2000 символов.");

        // 2. Проверяем фото и, если пользователь его записал, голосовой комментарий.
        var media = ReportMediaService.ExtractReportMedia(run.photosBase64, run.photoBase64, run.voiceBase64);

        // 3. Начинаем транзакцию
        await using var transaction = await conn.BeginTransactionAsync();

        // 4. Проверка на дубликат по clientRunId (идемпотентность)
        if (await _runRepository.ExistsByClientRunIdAsync(conn, transaction, run.clientRunId))
        {
            await transaction.RollbackAsync();
            return SaveRunResult.Duplicate;
        }

        // 5. Проверка, что пользователь существует и активен
        if (run.userId > 0)
        {
            await using var userCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE id = @userId AND is_active = TRUE", conn, transaction);
            userCmd.Parameters.AddWithValue("userId", run.userId);
            if (await userCmd.ExecuteScalarAsync() is null)
                throw new ArgumentException("Проверка пропущена: сотрудник не найден или отключён.");
        }

        // 6. Ищем коробку по QR-коду в рамках текущей транзакции.
        // Запрос оставлен здесь, чтобы искать QR и читать профиль атомарно с сохранением результата.
        await using var boxCmd = new NpgsqlCommand(@"
            SELECT c.id, q.id, c.title
            FROM boxes c
            JOIN qr_codes q ON q.id = c.qr_code_id
            WHERE q.code = ANY(@codes) AND q.is_active = TRUE AND c.is_active = TRUE
            LIMIT 1", conn, transaction);
        
        var codesParam = boxCmd.Parameters.AddWithValue("codes", QrHelper.ExpandQr(run.qrCode).ToArray());
        codesParam.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text;

        await using var cabReader = await boxCmd.ExecuteReaderAsync();
        if (!await cabReader.ReadAsync())
            throw new ArgumentException("Проверка пропущена: коробка для QR не найдена.");
        
        long boxId = cabReader.GetInt64(0);
        long qrCodeId = cabReader.GetInt64(1);
        string boxTitle = cabReader.GetString(2);
        await cabReader.CloseAsync();

        // 7. Получаем названия предметов чек-листа для снапшота
        var requestedIds = run.items.Select(item => item.itemId).ToArray();
        var itemNames = new Dictionary<long, string>();
        var expectedQuantities = new Dictionary<long, int>();
        
        await using var itemCmd = new NpgsqlCommand(@"
            SELECT id, name, expected_quantity
            FROM box_items
            WHERE box_id = @boxId AND is_active = TRUE AND id = ANY(@itemIds)", conn, transaction);
        itemCmd.Parameters.AddWithValue("boxId", boxId);
        itemCmd.Parameters.AddWithValue("itemIds", requestedIds);
        
        await using var itemReader = await itemCmd.ExecuteReaderAsync();
        while (await itemReader.ReadAsync())
        {
            long itemId = itemReader.GetInt64(0);
            itemNames[itemId] = itemReader.GetString(1);
            expectedQuantities[itemId] = itemReader.GetInt32(2);
        }
        await itemReader.CloseAsync();

        // Проверяем, что все запрошенные предметы найдены
        if (itemNames.Count != requestedIds.Length)
            throw new ArgumentException("Проверка пропущена: перечень коробки изменился, обновите данные в приложении.");

        // Убедимся, что приложение отправило все позиции активного перечня коробки.
        await using var countCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM box_items
            WHERE box_id = @boxId AND is_active = TRUE", conn, transaction);
        countCmd.Parameters.AddWithValue("boxId", boxId);
        if (Convert.ToInt64(await countCmd.ExecuteScalarAsync()) != requestedIds.Length)
            throw new ArgumentException("Проверка пропущена: в сохранённом списке коробки отсутствуют позиции.");

        // 8. Парсинг времени
        var startedAt = TimeHelper.ParseTime(run.startedAt, DateTimeOffset.UtcNow);
        var completedAt = TimeHelper.ParseTime(run.completedAt, DateTimeOffset.UtcNow);
        if (completedAt < startedAt)
            throw new ArgumentException("Проверка пропущена: время завершения раньше времени начала.");

        // 9. Создание записи инвентаризации
        // Для офлайн-работы пакет создаётся при первой синхронизации, а следующие
        // проверки с тем же clientBatchId продолжат заполнять именно его.
        long? batchId = null;
        if (run.clientBatchId is Guid clientBatchId)
        {
            batchId = await _runRepository.GetOrCreateBatchIdAsync(
                conn, transaction, clientBatchId, (int)run.userId,
                run.expectedBoxCount!.Value, startedAt.UtcDateTime);
            if (batchId is null)
                throw new ArgumentException("Проверка пропущена: параметры общей инвентаризации не совпадают.");

            if (!await _runRepository.IsQrAllowedInBatchAsync(conn, transaction, batchId.Value, qrCodeId))
                throw new ArgumentException("Эта коробка не включена в назначенную сотруднику инвентаризацию.");

            if (await _runRepository.BatchAlreadyContainsQrAsync(conn, transaction, batchId.Value, qrCodeId))
                throw new ArgumentException("Эта коробка уже проверена в текущей общей инвентаризации.");
        }

        long inventoryRunId = await _runRepository.CreateRunAsync(
            conn, transaction, run.clientRunId, boxId, (int)run.userId, qrCodeId,
            startedAt.UtcDateTime, completedAt.UtcDateTime, batchId, reportNote);
        
        if (inventoryRunId == -1) // Сработало ON CONFLICT DO NOTHING
        {
            await transaction.RollbackAsync();
            return SaveRunResult.Duplicate;
        }

        // 10. Сохраняем фотографии и необязательный голос в общей таблице вложений.
        if (media is { } attachments)
        {
            for (int photoIndex = 0; photoIndex < attachments.Photos.Count; photoIndex++)
            {
                await _runRepository.InsertRunAttachmentAsync(
                    conn, transaction, inventoryRunId, "photo", photoIndex,
                    "image/jpeg", attachments.Photos[photoIndex]);
            }

            if (attachments.Voice is { Length: > 0 } voice)
            {
                await _runRepository.InsertRunAttachmentAsync(
                    conn, transaction, inventoryRunId, "voice", 0,
                    "audio/wav", voice);
            }
        }

        // 11. Сохранение отметок по чек-листу
        foreach (var item in run.items)
        {
            int expectedQuantity = expectedQuantities[item.itemId];
            int actualQuantity = item.actualQuantity ??
                (item.status == "present" ? expectedQuantity : 0);
            string status = actualQuantity == 0
                ? "missing"
                : actualQuantity < expectedQuantity ? "partial" : "present";

            await _runRepository.InsertRunItemAsync(
                conn, transaction, inventoryRunId, item.itemId, itemNames[item.itemId], status,
                expectedQuantity, actualQuantity, item.comment);
        }

        // 12. Логирование события в журнал сессий
        int present = run.items.Count(item => item.actualQuantity is null
            ? item.status == "present"
            : item.actualQuantity.Value >= expectedQuantities[item.itemId]);
        int missing = run.items.Count(item => item.actualQuantity == 0 ||
            (item.actualQuantity is null && item.status == "missing"));
        int partial = run.items.Count - present - missing;
        await using var logCmd = new NpgsqlCommand(@"
            INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment, action_time)
            VALUES (NULLIF(@userId, 0), @qrCodeId, 'inventory', @comment, @completedAt)", conn, transaction);
        
        logCmd.Parameters.AddWithValue("userId", run.userId);
        logCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        logCmd.Parameters.AddWithValue("comment", $"Инвентаризация «{boxTitle}»: есть {present}, частично {partial}, нет {missing}");
        logCmd.Parameters.AddWithValue("completedAt", completedAt.UtcDateTime);
        await logCmd.ExecuteNonQueryAsync();

        if (batchId.HasValue)
            await _runRepository.CompleteBatchIfReadyAsync(conn, transaction, batchId.Value, completedAt.UtcDateTime);

        // 13. Коммит транзакции
        await transaction.CommitAsync();
        return SaveRunResult.Imported;
    }

    private enum SaveRunResult { Imported, Duplicate }
}

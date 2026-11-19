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
/// Полностью заменяет старый метод SaveRun из InventoryApi.cs.
/// Оркестрирует транзакцию: проверяет дубликаты, валидирует данные, 
/// декодирует медиа через ReportMediaService и сохраняет всё в БД через InventoryRunRepository.
/// </summary>
public class InventorySyncService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly InventoryRunRepository _runRepository;
    private readonly CabinetRepository _cabinetRepository;

    public InventorySyncService(
        NpgsqlDataSource dataSource, 
        InventoryRunRepository runRepository,
        CabinetRepository cabinetRepository)
    {
        _dataSource = dataSource;
        _runRepository = runRepository;
        _cabinetRepository = cabinetRepository;
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
        if (run.userId <= 0 || run.userId > int.MaxValue || string.IsNullOrWhiteSpace(run.qrCode))
            throw new ArgumentException("Проверка пропущена: не указан сотрудник или QR.");
        if (run.items == null || run.items.Count == 0)
            throw new ArgumentException("Проверка пропущена: список предметов пуст.");

        var duplicateItem = run.items.GroupBy(item => item.itemId).FirstOrDefault(group => group.Count() > 1);
        if (duplicateItem != null)
            throw new ArgumentException("Проверка пропущена: предмет повторяется в списке.");
        if (run.items.Any(item => item.itemId <= 0 || (item.status != "present" && item.status != "missing")))
            throw new ArgumentException("Проверка пропущена: для каждого предмета выберите «Есть» или «Нет».");

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
        await using var userCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE id = @userId AND is_active = TRUE", conn, transaction);
        userCmd.Parameters.AddWithValue("userId", run.userId);
        if (await userCmd.ExecuteScalarAsync() is null)
            throw new ArgumentException("Проверка пропущена: сотрудник не найден или отключён.");

        // 6. Поиск шкафа по QR-коду (используем CabinetRepository, но в рамках нашей транзакции)
        // В оригинале был прямой SQL, здесь мы вызываем метод репозитория, но нам нужно передать транзакцию.
        // Чтобы не усложнять интерфейс CabinetRepository, выполним этот конкретный запрос здесь, 
        // так как он требует ExpandQr и специфичен для процесса сохранения.
        await using var cabinetCmd = new NpgsqlCommand(@"
            SELECT c.id, q.id, c.title
            FROM cabinet_profiles c
            JOIN qr_codes q ON q.id = c.qr_code_id
            WHERE q.code = ANY(@codes) AND q.is_active = TRUE AND c.is_active = TRUE
            LIMIT 1", conn, transaction);
        
        var codesParam = cabinetCmd.Parameters.AddWithValue("codes", QrHelper.ExpandQr(run.qrCode).ToArray());
        codesParam.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text;

        await using var cabReader = await cabinetCmd.ExecuteReaderAsync();
        if (!await cabReader.ReadAsync())
            throw new ArgumentException("Проверка пропущена: шкаф для QR не найден.");
        
        long cabinetId = cabReader.GetInt64(0);
        long qrCodeId = cabReader.GetInt64(1);
        string cabinetTitle = cabReader.GetString(2);
        await cabReader.CloseAsync();

        // 7. Получаем названия предметов чек-листа для снапшота
        var requestedIds = run.items.Select(item => item.itemId).ToArray();
        var itemNames = new Dictionary<long, string>();
        
        await using var itemCmd = new NpgsqlCommand(@"
            SELECT id, item_name
            FROM cabinet_checklist_items
            WHERE cabinet_id = @cabinetId AND is_active = TRUE AND id = ANY(@itemIds)", conn, transaction);
        itemCmd.Parameters.AddWithValue("cabinetId", cabinetId);
        itemCmd.Parameters.AddWithValue("itemIds", requestedIds);
        
        await using var itemReader = await itemCmd.ExecuteReaderAsync();
        while (await itemReader.ReadAsync())
            itemNames[itemReader.GetInt64(0)] = itemReader.GetString(1);
        await itemReader.CloseAsync();

        // Проверяем, что все запрошенные предметы найдены
        if (itemNames.Count != requestedIds.Length)
            throw new ArgumentException("Проверка пропущена: перечень шкафа изменился, обновите данные в приложении.");

        // Проверяем, что в сохраненном перечне ровно столько предметов, сколько в активном чек-листе шкафа
        await using var countCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM cabinet_checklist_items
            WHERE cabinet_id = @cabinetId AND is_active = TRUE", conn, transaction);
        countCmd.Parameters.AddWithValue("cabinetId", cabinetId);
        if (Convert.ToInt64(await countCmd.ExecuteScalarAsync()) != requestedIds.Length)
            throw new ArgumentException("Проверка пропущена: в сохранённом перечне не все предметы.");

        // 8. Парсинг времени
        var startedAt = TimeHelper.ParseTime(run.startedAt, DateTimeOffset.UtcNow);
        var completedAt = TimeHelper.ParseTime(run.completedAt, DateTimeOffset.UtcNow);
        if (completedAt < startedAt)
            throw new ArgumentException("Проверка пропущена: время завершения раньше времени начала.");

        // 9. Создание записи инвентаризации
        long inventoryRunId = await _runRepository.CreateRunAsync(
            conn, transaction, run.clientRunId, cabinetId, (int)run.userId, qrCodeId, 
            startedAt.UtcDateTime, completedAt.UtcDateTime);
        
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
            await _runRepository.InsertRunItemAsync(
                conn, transaction, inventoryRunId, item.itemId, itemNames[item.itemId], item.status, item.comment);
        }

        // 12. Логирование события в журнал сессий
        int present = run.items.Count(item => item.status == "present");
        int missing = run.items.Count - present;
        await using var logCmd = new NpgsqlCommand(@"
            INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment, action_time)
            VALUES (@userId, @qrCodeId, 'inventory', @comment, @completedAt)", conn, transaction);
        
        logCmd.Parameters.AddWithValue("userId", run.userId);
        logCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        logCmd.Parameters.AddWithValue("comment", $"Инвентаризация «{cabinetTitle}»: есть {present}, нет {missing}");
        logCmd.Parameters.AddWithValue("completedAt", completedAt.UtcDateTime);
        await logCmd.ExecuteNonQueryAsync();

        // 13. Коммит транзакции
        await transaction.CommitAsync();
        return SaveRunResult.Imported;
    }

    private enum SaveRunResult { Imported, Duplicate }
}

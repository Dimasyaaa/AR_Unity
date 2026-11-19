using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backend.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Backend.Data;

/// <summary>
/// Слой доступа к таблицам, связанным со шкафами:
/// - cabinet_profiles (карточка шкафа + legacy-фото reference_photo)
/// - cabinet_checklist_items (чек-лист предметов)
/// - cabinet_photos (до 2 листов фотографий)
/// Перенесён SQL из InventoryApi.cs (эндпоинты /api/inventory/cabinets, cabinet-photo
/// и все эндпоинты /api/admin/inventory/cabinets...).
/// </summary>
public class CabinetRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public CabinetRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Поиск шкафа по QR-коду (GET /api/inventory/cabinets).
    /// ExpandQr позволяет найти шкаф, даже если в БД хранится только "хвост" URL.
    /// Возвращает кортеж с основными полями + флаг наличия фото и его тип.
    /// </summary>
    public async Task<(long cabinetId, long qrCodeId, string title, string code, bool hasPhoto, string contentType)?>
        FindByQrCodeAsync(string qrCode)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT c.id, q.id, c.title, q.code,
                   c.reference_photo IS NOT NULL, c.reference_photo_content_type
            FROM cabinet_profiles c
            JOIN qr_codes q ON q.id = c.qr_code_id
            WHERE q.code = ANY(@codes)
              AND q.is_active = TRUE
              AND c.is_active = TRUE
            LIMIT 1", conn);

        var codesParam = cmd.Parameters.AddWithValue("codes", Helpers.QrHelper.ExpandQr(qrCode).ToArray());
        codesParam.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            reader.GetString(5)
        );
    }

    /// <summary>
    /// Чек-лист предметов шкафа (используется в GET /api/inventory/cabinets).
    /// </summary>
    public async Task<List<object>> GetChecklistItemsAsync(long cabinetId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, item_name, COALESCE(description, ''), sort_order
            FROM cabinet_checklist_items
            WHERE cabinet_id = @cabinetId AND is_active = TRUE
            ORDER BY sort_order, id", conn);
        cmd.Parameters.AddWithValue("cabinetId", cabinetId);

        var items = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                name = reader.GetString(1),
                description = reader.GetString(2),
                sortOrder = reader.GetInt32(3)
            });
        }
        return items;
    }

    /// <summary>
    /// Список фотографий шкафа (GET /api/inventory/cabinets).
    /// Возвращает URL-ы вида /api/inventory/cabinet-photo?qrCode=...&amp;pageNumber=N.
    /// </summary>
    public async Task<List<object>> GetCabinetPhotosAsync(long cabinetId, string code)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT page_number, caption, content_type
            FROM cabinet_photos
            WHERE cabinet_id = @cabinetId
            ORDER BY page_number", conn);
        cmd.Parameters.AddWithValue("cabinetId", cabinetId);

        var photos = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var page = reader.GetInt16(0);
            photos.Add(new
            {
                pageNumber = page,
                caption = reader.GetString(1),
                contentType = reader.GetString(2),
                photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(code)}&pageNumber={page}"
            });
        }
        return photos;
    }

    /// <summary>
    /// Получение байт фотографии шкафа (GET /api/inventory/cabinet-photo).
    /// Поддерживает legacy-фото (reference_photo, page=1) и новые cabinet_photos.
    /// Возвращает null, если фото не найдено.
    /// </summary>
    public async Task<(byte[] bytes, string contentType)?> GetCabinetPhotoAsync(string qrCode, int pageNumber)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT COALESCE(p.image_data, CASE WHEN @page = 1 THEN c.reference_photo END),
                   COALESCE(p.content_type, c.reference_photo_content_type)
            FROM cabinet_profiles c
            JOIN qr_codes q ON q.id = c.qr_code_id
            LEFT JOIN cabinet_photos p ON p.cabinet_id = c.id AND p.page_number = @page
            WHERE q.code = ANY(@codes)
              AND q.is_active = TRUE AND c.is_active = TRUE
              AND (p.image_data IS NOT NULL OR (@page = 1 AND c.reference_photo IS NOT NULL))
            LIMIT 1", conn);

        var codesParam = cmd.Parameters.AddWithValue("codes", Helpers.QrHelper.ExpandQr(qrCode).ToArray());
        codesParam.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
        cmd.Parameters.AddWithValue("page", pageNumber);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync())
            return null;

        var bytes = (byte[])reader[0];
        var contentType = reader.GetString(1);
        return (bytes, contentType);
    }

    /// <summary>
    /// Список всех QR-кодов и связанных шкафов для админки (GET /api/admin/inventory/cabinets).
    /// Возвращает анонимные объекты с флагом photoAvailable и количеством предметов.
    /// </summary>
    public async Task<List<object>> GetAllCabinetsAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT q.id, q.code, q.object_name, c.id, c.title,
                   (COALESCE(c.reference_photo IS NOT NULL, FALSE) OR EXISTS (
                       SELECT 1 FROM cabinet_photos p WHERE p.cabinet_id = c.id
                   )),
                   (SELECT COUNT(*) FROM cabinet_checklist_items i
                    WHERE i.cabinet_id = c.id AND i.is_active = TRUE)
            FROM qr_codes q
            LEFT JOIN cabinet_profiles c ON c.qr_code_id = q.id
            ORDER BY q.object_name, q.id", conn);

        var cabinets = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            cabinets.Add(new
            {
                qrCodeId = reader.GetInt64(0),
                qrCode = reader.GetString(1),
                objectName = reader.GetString(2),
                cabinetId = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3),
                title = reader.IsDBNull(4) ? reader.GetString(2) : reader.GetString(4),
                photoAvailable = reader.GetBoolean(5),
                itemCount = reader.GetInt64(6)
            });
        }
        return cabinets;
    }

    /// <summary>
    /// Полные данные шкафа по ID QR-кода (GET /api/admin/inventory/cabinets/{qrCodeId}).
    /// Возвращает null, если QR не найден.
    /// </summary>
    public async Task<(long cabinetId, string code, string objectName, string title, bool photoAvailable)?>
        GetCabinetByQrCodeIdAsync(long qrCodeId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT q.code, q.object_name, c.id, c.title,
                   (c.reference_photo IS NOT NULL OR EXISTS (SELECT 1 FROM cabinet_photos p WHERE p.cabinet_id = c.id))
            FROM qr_codes q LEFT JOIN cabinet_profiles c ON c.qr_code_id = q.id
            WHERE q.id = @qrCodeId", conn);
        cmd.Parameters.AddWithValue("qrCodeId", qrCodeId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(3) ? reader.GetString(1) : reader.GetString(3),
            !reader.IsDBNull(4) && reader.GetBoolean(4)
        );
    }

    /// <summary>
    /// Транзакционное сохранение карточки шкафа (PUT /api/admin/inventory/cabinets/{qrCodeId}).
    /// Обновляет/создаёт cabinet_profiles, чек-лист и фотографии.
    /// Возвращает id созданного/обновлённого шкафа, либо null, если QR не найден.
    /// </summary>
    public async Task<long?> UpsertCabinetAsync(
        long qrCodeId,
        string title,
        List<AdminChecklistItemDto> items,
        List<(int page, byte[] bytes, string type, string caption)> photoUploads,
        int[] removalPages)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        // 1. UPSERT cabinet_profiles
        await using var upsertCmd = new NpgsqlCommand(@"
            INSERT INTO cabinet_profiles
                (qr_code_id, title, reference_photo, reference_photo_content_type, is_active)
            SELECT q.id, @title, @photo, @contentType, TRUE
            FROM qr_codes q WHERE q.id = @qrCodeId
            ON CONFLICT (qr_code_id) DO UPDATE SET
                title = EXCLUDED.title,
                reference_photo = COALESCE(EXCLUDED.reference_photo, cabinet_profiles.reference_photo),
                reference_photo_content_type = CASE WHEN EXCLUDED.reference_photo IS NULL
                    THEN cabinet_profiles.reference_photo_content_type
                    ELSE EXCLUDED.reference_photo_content_type END,
                is_active = TRUE,
                updated_at = NOW()
            RETURNING id", conn, transaction);

        upsertCmd.Parameters.AddWithValue("qrCodeId", qrCodeId);
        upsertCmd.Parameters.AddWithValue("title", title);

        var firstPage = photoUploads.FirstOrDefault(p => p.page == 1);
        upsertCmd.Parameters.Add("photo", NpgsqlDbType.Bytea).Value =
            firstPage.bytes is null ? DBNull.Value : firstPage.bytes;
        upsertCmd.Parameters.AddWithValue("contentType",
            firstPage.type ?? "image/jpeg");

        var result = await upsertCmd.ExecuteScalarAsync();
        if (result is null)
        {
            await transaction.RollbackAsync();
            return null;
        }
        long cabinetId = Convert.ToInt64(result);

        // 2. Удаление запрошенных страниц фото
        foreach (var page in removalPages)
        {
            await using var removeCmd = new NpgsqlCommand(
                "DELETE FROM cabinet_photos WHERE cabinet_id = @cabinetId AND page_number = @page",
                conn, transaction);
            removeCmd.Parameters.AddWithValue("cabinetId", cabinetId);
            removeCmd.Parameters.AddWithValue("page", page);
            await removeCmd.ExecuteNonQueryAsync();

            if (page == 1)
            {
                await using var clearLegacy = new NpgsqlCommand(@"
                    UPDATE cabinet_profiles
                    SET reference_photo = NULL, reference_photo_content_type = 'image/jpeg', updated_at = NOW()
                    WHERE id = @cabinetId", conn, transaction);
                clearLegacy.Parameters.AddWithValue("cabinetId", cabinetId);
                await clearLegacy.ExecuteNonQueryAsync();
            }
        }

        // 3. Загрузка новых фото
        foreach (var upload in photoUploads)
        {
            await using var photoCmd = new NpgsqlCommand(@"
                INSERT INTO cabinet_photos (cabinet_id, page_number, image_data, content_type, caption, updated_at)
                VALUES (@cabinetId, @page, @image, @type, @caption, NOW())
                ON CONFLICT (cabinet_id, page_number) DO UPDATE SET
                    image_data = EXCLUDED.image_data,
                    content_type = EXCLUDED.content_type,
                    caption = EXCLUDED.caption,
                    updated_at = NOW()", conn, transaction);
            photoCmd.Parameters.AddWithValue("cabinetId", cabinetId);
            photoCmd.Parameters.AddWithValue("page", upload.page);
            photoCmd.Parameters.Add("image", NpgsqlDbType.Bytea).Value = upload.bytes;
            photoCmd.Parameters.AddWithValue("type", upload.type);
            photoCmd.Parameters.AddWithValue("caption", upload.caption);
            await photoCmd.ExecuteNonQueryAsync();
        }

        // 4. Архивация старого чек-листа (помечаем неактивным с уникальным именем, чтобы UNIQUE-констрейнт не мешал)
        await using var deactivateCmd = new NpgsqlCommand(@"
            UPDATE cabinet_checklist_items
            SET is_active = FALSE,
                item_name = '__inventory_archived_' || id::text || '_' || txid_current()::text
            WHERE cabinet_id = @cabinetId AND is_active = TRUE", conn, transaction);
        deactivateCmd.Parameters.AddWithValue("cabinetId", cabinetId);
        await deactivateCmd.ExecuteNonQueryAsync();

        // 5. Вставка/обновление пунктов чек-листа
        foreach (var item in items)
        {
            if (item.id > 0)
            {
                await using var updateCmd = new NpgsqlCommand(@"
                    UPDATE cabinet_checklist_items
                    SET item_name = @name, description = @description, sort_order = @sortOrder, is_active = TRUE
                    WHERE id = @id AND cabinet_id = @cabinetId", conn, transaction);
                updateCmd.Parameters.AddWithValue("id", item.id);
                updateCmd.Parameters.AddWithValue("cabinetId", cabinetId);
                updateCmd.Parameters.AddWithValue("name", item.name!);
                updateCmd.Parameters.Add("description", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(item.description) ? DBNull.Value : item.description!;
                updateCmd.Parameters.AddWithValue("sortOrder", item.sortOrder);

                if (await updateCmd.ExecuteNonQueryAsync() == 0)
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException("Пункт списка не относится к выбранному шкафу.");
                }
            }
            else
            {
                await using var insertCmd = new NpgsqlCommand(@"
                    INSERT INTO cabinet_checklist_items (cabinet_id, item_name, description, sort_order, is_active)
                    VALUES (@cabinetId, @name, @description, @sortOrder, TRUE)
                    ON CONFLICT (cabinet_id, item_name) DO UPDATE SET
                        description = EXCLUDED.description,
                        sort_order = EXCLUDED.sort_order,
                        is_active = TRUE", conn, transaction);
                insertCmd.Parameters.AddWithValue("cabinetId", cabinetId);
                insertCmd.Parameters.AddWithValue("name", item.name!);
                insertCmd.Parameters.Add("description", NpgsqlDbType.Text).Value =
                    string.IsNullOrWhiteSpace(item.description) ? DBNull.Value : item.description!;
                insertCmd.Parameters.AddWithValue("sortOrder", item.sortOrder);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();
        return cabinetId;
    }
}
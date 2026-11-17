using System.Buffers.Binary;
using System.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using NpgsqlTypes;

public static class InventoryApi
{
    private const int MaxReportPhotoBytes = 4 * 1024 * 1024;
    private const int MaxReportVoiceBytes = 44 + 60 * 16000 * 2;

    public static IEndpointRouteBuilder MapInventoryApi(
        this IEndpointRouteBuilder endpoints,
        string connectionString)
    {
        endpoints.MapGet("/api/inventory/cabinets", (string? qrCode) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите qrCode" });

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();

            var qrValues = ExpandQr(qrCode).ToArray();
            long cabinetId;
            long qrCodeId;
            string title;
            string normalizedCode;
            bool hasPhoto;
            string contentType;

            using (var command = new NpgsqlCommand(@"
                SELECT c.id, q.id, c.title, q.code,
                       c.reference_photo IS NOT NULL, c.reference_photo_content_type
                FROM cabinet_profiles c
                JOIN qr_codes q ON q.id = c.qr_code_id
                WHERE q.code = ANY(@codes)
                  AND q.is_active = TRUE
                  AND c.is_active = TRUE
                LIMIT 1
            ", connection))
            {
                command.Parameters.AddWithValue("codes", qrValues);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return Results.NotFound(new { success = false, message = "Шкаф для этого QR-кода не найден" });

                cabinetId = reader.GetInt64(0);
                qrCodeId = reader.GetInt64(1);
                title = reader.GetString(2);
                normalizedCode = reader.GetString(3);
                hasPhoto = reader.GetBoolean(4);
                contentType = reader.GetString(5);
            }

            var items = new List<object>();
            using (var command = new NpgsqlCommand(@"
                SELECT id, item_name, COALESCE(description, ''), sort_order
                FROM cabinet_checklist_items
                WHERE cabinet_id = @cabinetId AND is_active = TRUE
                ORDER BY sort_order, id
            ", connection))
            {
                command.Parameters.AddWithValue("cabinetId", cabinetId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new
                    {
                        id = reader.GetInt64(0),
                        name = reader.GetString(1),
                        description = reader.GetString(2),
                        sortOrder = reader.GetInt32(3)
                    });
                }
            }

            var photos = new List<object>();
            using (var command = new NpgsqlCommand(@"
                SELECT page_number, caption, content_type
                FROM cabinet_photos
                WHERE cabinet_id = @cabinetId
                ORDER BY page_number
            ", connection))
            {
                command.Parameters.AddWithValue("cabinetId", cabinetId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    photos.Add(new
                    {
                        pageNumber = reader.GetInt16(0),
                        caption = reader.GetString(1),
                        contentType = reader.GetString(2),
                        photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(normalizedCode)}&pageNumber={reader.GetInt16(0)}"
                    });
            }
            if (photos.Count == 0 && hasPhoto)
                photos.Add(new
                {
                    pageNumber = 1,
                    caption = "Лист 1",
                    contentType,
                    photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(normalizedCode)}&pageNumber=1"
                });
            hasPhoto = photos.Count > 0;

            return Results.Ok(new
            {
                success = true,
                cabinetId,
                qrCodeId,
                qrCode = normalizedCode,
                title,
                photoAvailable = hasPhoto,
                photoContentType = contentType,
                photoUrl = hasPhoto ? "/api/inventory/cabinet-photo?qrCode=" + Uri.EscapeDataString(normalizedCode) + "&pageNumber=1" : null,
                photos,
                items
            });
        })
        .WithTags("Inventory")
        .WithSummary("Получить шкаф и перечень по QR-коду")
        .WithDescription("Возвращает карточку шкафа и активный список предметов. Фотография хранится в PostgreSQL BYTEA и доступна отдельным запросом.")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        endpoints.MapGet("/api/inventory/cabinet-photo", (string? qrCode, int? pageNumber) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите qrCode" });

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            int page = Math.Clamp(pageNumber ?? 1, 1, 2);
            using var command = new NpgsqlCommand(@"
                SELECT COALESCE(p.image_data, CASE WHEN @page = 1 THEN c.reference_photo END),
                       COALESCE(p.content_type, c.reference_photo_content_type)
                FROM cabinet_profiles c
                JOIN qr_codes q ON q.id = c.qr_code_id
                LEFT JOIN cabinet_photos p ON p.cabinet_id = c.id AND p.page_number = @page
                WHERE q.code = ANY(@codes)
                  AND q.is_active = TRUE AND c.is_active = TRUE
                  AND (p.image_data IS NOT NULL OR (@page = 1 AND c.reference_photo IS NOT NULL))
                LIMIT 1
            ", connection);
            command.Parameters.AddWithValue("codes", ExpandQr(qrCode).ToArray());
            command.Parameters.AddWithValue("page", page);

            using var reader = command.ExecuteReader(CommandBehavior.SequentialAccess);
            if (!reader.Read())
                return Results.NotFound(new { success = false, message = "Фотография шкафа не найдена" });

            var bytes = (byte[])reader[0];
            var contentType = reader.GetString(1);
            return Results.Bytes(bytes, contentType, enableRangeProcessing: false);
        })
        .WithTags("Inventory")
        .WithSummary("Получить фотографию шкафа")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        endpoints.MapPost("/api/sync/inventory-runs", (InventoryRunSyncPayload payload) =>
        {
            if (payload?.items == null)
                return Results.BadRequest(new { success = false, message = "Список проверок отсутствует" });

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            int imported = 0;
            int duplicates = 0;
            int skipped = 0;
            var errors = new List<string>();

            foreach (var run in payload.items)
            {
                try
                {
                    var result = SaveRun(connection, run);
                    if (result == SaveRunResult.Imported) imported++;
                    else if (result == SaveRunResult.Duplicate) duplicates++;
                    else skipped++;
                }
                catch (ArgumentException exception)
                {
                    skipped++;
                    errors.Add(exception.Message);
                }
            }

            return Results.Ok(new { imported, duplicates, skipped, errors });
        })
        .WithTags("Inventory")
        .WithSummary("Синхронизировать завершённые инвентаризации")
        .WithDescription("Принимает сохранённые офлайн-проверки. clientRunId обеспечивает безопасный повтор запроса без дубликатов.")
        .Produces(200)
        .Produces(400);

        endpoints.MapGet("/api/admin/inventory/cabinets", () =>
        {
            var cabinets = new List<object>();
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(@"
                SELECT q.id, q.code, q.object_name, c.id, c.title,
                       (COALESCE(c.reference_photo IS NOT NULL, FALSE) OR EXISTS (
                           SELECT 1 FROM cabinet_photos p WHERE p.cabinet_id = c.id
                       )),
                       (SELECT COUNT(*) FROM cabinet_checklist_items i
                        WHERE i.cabinet_id = c.id AND i.is_active = TRUE)
                FROM qr_codes q
                LEFT JOIN cabinet_profiles c ON c.qr_code_id = q.id
                ORDER BY q.object_name, q.id
            ", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
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
            return Results.Ok(cabinets);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Список QR-кодов и настроенных шкафов")
        .Produces(200);

        endpoints.MapGet("/api/admin/inventory/cabinets/{qrCodeId:long}", (long qrCodeId) =>
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            long cabinetId;
            string code;
            string objectName;
            string title;
            bool photoAvailable;
            using (var command = new NpgsqlCommand(@"
                SELECT q.code, q.object_name, c.id, c.title,
                       (c.reference_photo IS NOT NULL OR EXISTS (SELECT 1 FROM cabinet_photos p WHERE p.cabinet_id = c.id))
                FROM qr_codes q LEFT JOIN cabinet_profiles c ON c.qr_code_id = q.id
                WHERE q.id = @qrCodeId
            ", connection))
            {
                command.Parameters.AddWithValue("qrCodeId", qrCodeId);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return Results.NotFound(new { message = "QR-код не найден" });
                code = reader.GetString(0);
                objectName = reader.GetString(1);
                cabinetId = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                title = reader.IsDBNull(3) ? objectName : reader.GetString(3);
                photoAvailable = !reader.IsDBNull(4) && reader.GetBoolean(4);
            }
            var items = new List<object>();
            if (cabinetId > 0)
            {
                using var command = new NpgsqlCommand(@"
                    SELECT id, item_name, COALESCE(description, ''), sort_order
                    FROM cabinet_checklist_items WHERE cabinet_id = @cabinetId AND is_active = TRUE
                    ORDER BY sort_order, id
                ", connection);
                command.Parameters.AddWithValue("cabinetId", cabinetId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) items.Add(new
                {
                    id = reader.GetInt64(0), name = reader.GetString(1),
                    description = reader.GetString(2), sortOrder = reader.GetInt32(3)
                });
            }
            var photos = new List<object>();
            if (cabinetId > 0)
            {
                using var photoCommand = new NpgsqlCommand(@"
                    SELECT page_number, caption, content_type
                    FROM cabinet_photos WHERE cabinet_id = @cabinetId ORDER BY page_number
                ", connection);
                photoCommand.Parameters.AddWithValue("cabinetId", cabinetId);
                using var photoReader = photoCommand.ExecuteReader();
                while (photoReader.Read()) photos.Add(new
                {
                    pageNumber = photoReader.GetInt16(0),
                    caption = photoReader.GetString(1),
                    contentType = photoReader.GetString(2),
                    photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(code)}&pageNumber={photoReader.GetInt16(0)}"
                });
            }
            if (photos.Count == 0 && photoAvailable)
                photos.Add(new { pageNumber = 1, caption = "Лист 1", contentType = "image/jpeg", photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(code)}&pageNumber=1" });
            return Results.Ok(new { qrCodeId, qrCode = code, objectName, cabinetId, title, photoAvailable, photos, items });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Получить настройки шкафа и его чек-лист")
        .Produces(200)
        .Produces(404);

        endpoints.MapPut("/api/admin/inventory/cabinets/{qrCodeId:long}", (long qrCodeId, AdminCabinetDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.title) || dto.items == null)
                return Results.BadRequest(new { message = "Название шкафа и список предметов обязательны" });
            var cleanedItems = dto.items
                .Select((item, index) => new AdminChecklistItemDto
                {
                    id = item.id,
                    name = item.name?.Trim() ?? string.Empty,
                    description = item.description?.Trim() ?? string.Empty,
                    sortOrder = item.sortOrder == 0 ? (index + 1) * 10 : item.sortOrder
                }).ToList();
            if (cleanedItems.Any(item => string.IsNullOrWhiteSpace(item.name)))
                return Results.BadRequest(new { message = "У каждого пункта должно быть название" });
            if (cleanedItems.GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                return Results.BadRequest(new { message = "Названия предметов в одном шкафу не должны повторяться" });

            var photoUploads = new List<(int page, byte[] bytes, string type, string caption)>();
            var requestedPhotos = dto.photos ?? new List<AdminCabinetPhotoDto>();
            if (requestedPhotos.GroupBy(photo => photo.pageNumber).Any(group => group.Count() > 1) ||
                requestedPhotos.Any(photo => photo.pageNumber is < 1 or > 2))
                return Results.BadRequest(new { message = "Можно загрузить не более двух фотографий: лист 1 и лист 2" });
            foreach (var requestedPhoto in requestedPhotos)
            {
                if (string.IsNullOrWhiteSpace(requestedPhoto.imageBase64)) continue;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(requestedPhoto.imageBase64); }
                catch (FormatException) { return Results.BadRequest(new { message = "Файл фотографии повреждён" }); }
                if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
                    return Results.BadRequest(new { message = "Каждая фотография должна быть не больше 8 МБ" });
                if (requestedPhoto.contentType is not ("image/jpeg" or "image/png" or "image/webp"))
                    return Results.BadRequest(new { message = "Поддерживаются JPEG, PNG и WebP" });
                photoUploads.Add((requestedPhoto.pageNumber, bytes, requestedPhoto.contentType!,
                    string.IsNullOrWhiteSpace(requestedPhoto.caption) ? $"Лист {requestedPhoto.pageNumber}" : requestedPhoto.caption.Trim()));
            }
            if (photoUploads.Count == 0 && !string.IsNullOrWhiteSpace(dto.photoBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(dto.photoBase64);
                    if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
                        return Results.BadRequest(new { message = "Фотография должна быть не больше 8 МБ" });
                    if (dto.photoContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                        return Results.BadRequest(new { message = "Поддерживаются JPEG, PNG и WebP" });
                    photoUploads.Add((1, bytes, dto.photoContentType!, "Лист 1"));
                }
                catch (FormatException) { return Results.BadRequest(new { message = "Файл фотографии повреждён" }); }
            }

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            long cabinetId;
            using (var upsert = new NpgsqlCommand(@"
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
                RETURNING id
            ", connection, transaction))
            {
                upsert.Parameters.AddWithValue("qrCodeId", qrCodeId);
                upsert.Parameters.AddWithValue("title", dto.title.Trim());
                var firstPage = photoUploads.FirstOrDefault(photo => photo.page == 1);
                upsert.Parameters.Add("photo", NpgsqlDbType.Bytea).Value = firstPage.bytes is null ? DBNull.Value : firstPage.bytes;
                upsert.Parameters.AddWithValue("contentType", firstPage.type ?? dto.photoContentType ?? "image/jpeg");
                var result = upsert.ExecuteScalar();
                if (result is null)
                { transaction.Rollback(); return Results.NotFound(new { message = "QR-код не найден" }); }
                cabinetId = Convert.ToInt64(result);
            }

            var removalPages = (dto.removePhotoPages ?? new List<int>()).Distinct().ToArray();
            if (removalPages.Any(page => page is < 1 or > 2))
                return Results.BadRequest(new { message = "Укажите номер фотографии: 1 или 2" });
            foreach (var page in removalPages)
            {
                using var removePhoto = new NpgsqlCommand(
                    "DELETE FROM cabinet_photos WHERE cabinet_id = @cabinetId AND page_number = @page",
                    connection, transaction);
                removePhoto.Parameters.AddWithValue("cabinetId", cabinetId);
                removePhoto.Parameters.AddWithValue("page", page);
                removePhoto.ExecuteNonQuery();
                if (page == 1)
                {
                    using var clearLegacyPhoto = new NpgsqlCommand(@"
                        UPDATE cabinet_profiles
                        SET reference_photo = NULL, reference_photo_content_type = 'image/jpeg', updated_at = NOW()
                        WHERE id = @cabinetId
                    ", connection, transaction);
                    clearLegacyPhoto.Parameters.AddWithValue("cabinetId", cabinetId);
                    clearLegacyPhoto.ExecuteNonQuery();
                }
            }

            foreach (var upload in photoUploads)
            {
                using var photoCommand = new NpgsqlCommand(@"
                    INSERT INTO cabinet_photos (cabinet_id, page_number, image_data, content_type, caption, updated_at)
                    VALUES (@cabinetId, @page, @image, @type, @caption, NOW())
                    ON CONFLICT (cabinet_id, page_number) DO UPDATE SET
                        image_data = EXCLUDED.image_data,
                        content_type = EXCLUDED.content_type,
                        caption = EXCLUDED.caption,
                        updated_at = NOW()
                ", connection, transaction);
                photoCommand.Parameters.AddWithValue("cabinetId", cabinetId);
                photoCommand.Parameters.AddWithValue("page", upload.page);
                photoCommand.Parameters.Add("image", NpgsqlDbType.Bytea).Value = upload.bytes;
                photoCommand.Parameters.AddWithValue("type", upload.type);
                photoCommand.Parameters.AddWithValue("caption", upload.caption);
                photoCommand.ExecuteNonQuery();
            }

            using (var deactivate = new NpgsqlCommand(@"
                UPDATE cabinet_checklist_items
                SET is_active = FALSE,
                    item_name = '__inventory_archived_' || id::text || '_' || txid_current()::text
                WHERE cabinet_id = @cabinetId AND is_active = TRUE
            ", connection, transaction))
            {
                deactivate.Parameters.AddWithValue("cabinetId", cabinetId);
                deactivate.ExecuteNonQuery();
            }
            foreach (var item in cleanedItems)
            {
                if (item.id > 0)
                {
                    using var update = new NpgsqlCommand(@"
                        UPDATE cabinet_checklist_items
                        SET item_name = @name, description = @description, sort_order = @sortOrder, is_active = TRUE
                        WHERE id = @id AND cabinet_id = @cabinetId
                    ", connection, transaction);
                    update.Parameters.AddWithValue("id", item.id);
                    update.Parameters.AddWithValue("cabinetId", cabinetId);
                    update.Parameters.AddWithValue("name", item.name!);
                    update.Parameters.Add("description", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(item.description)
                        ? DBNull.Value
                        : item.description!;
                    update.Parameters.AddWithValue("sortOrder", item.sortOrder);
                    if (update.ExecuteNonQuery() == 0)
                    { transaction.Rollback(); return Results.BadRequest(new { message = "Пункт списка не относится к выбранному шкафу. Обновите страницу." }); }
                }
                else
                {
                    using var insert = new NpgsqlCommand(@"
                        INSERT INTO cabinet_checklist_items (cabinet_id, item_name, description, sort_order, is_active)
                        VALUES (@cabinetId, @name, @description, @sortOrder, TRUE)
                        ON CONFLICT (cabinet_id, item_name) DO UPDATE SET
                            description = EXCLUDED.description,
                            sort_order = EXCLUDED.sort_order,
                            is_active = TRUE
                    ", connection, transaction);
                    insert.Parameters.AddWithValue("cabinetId", cabinetId);
                    insert.Parameters.AddWithValue("name", item.name!);
                    insert.Parameters.Add("description", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(item.description)
                        ? DBNull.Value
                        : item.description!;
                    insert.Parameters.AddWithValue("sortOrder", item.sortOrder);
                    insert.ExecuteNonQuery();
                }
            }
            transaction.Commit();
            return Results.Ok(new { success = true, message = "Данные шкафа сохранены" });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Создать или обновить карточку шкафа, фотографию и перечень")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        endpoints.MapDelete("/api/admin/inventory/cabinets/{qrCodeId:long}", (long qrCodeId) =>
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var deletion = DeleteQrDependencies(connection, transaction, qrCodeId, requireCabinet: true);
            if (deletion is null)
            {
                transaction.Rollback();
                return Results.NotFound(new { success = false, message = "Шкаф не найден" });
            }

            using (var command = new NpgsqlCommand(
                "DELETE FROM qr_codes WHERE id = @qrCodeId", connection, transaction))
            {
                command.Parameters.AddWithValue("qrCodeId", qrCodeId);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return Results.Ok(new
            {
                success = true,
                message = "Шкаф, его QR-код и история удалены",
                deletedRunItems = deletion.Value.DeletedRunItems,
                deletedRuns = deletion.Value.DeletedRuns,
                deletedChecklistItems = deletion.Value.DeletedChecklistItems,
                deletedPhotos = deletion.Value.DeletedPhotos,
                deletedEvents = deletion.Value.DeletedEvents
            });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Полностью удалить шкаф вместе с QR-кодом и историей")
        .WithDescription("Транзакционно удаляет историю и отметки, чек-лист, фотографии, шкаф и связанный QR-код.")
        .Produces(200)
        .Produces(404);

        endpoints.MapGet("/api/admin/inventory/runs", (int? limit) =>
        {
            var runs = new List<object>();
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(@"
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
                LIMIT @limit
            ", connection);
            command.Parameters.AddWithValue("limit", Math.Clamp(limit ?? 100, 1, 500));
            using var reader = command.ExecuteReader();
            while (reader.Read())
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
            return Results.Ok(runs);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Последние отправленные инвентаризации")
        .WithDescription("Возвращает сводку по сотруднику, шкафу и отметкам. Подробности доступны отдельным запросом.")
        .Produces(200);

        endpoints.MapGet("/api/admin/inventory/runs/{runId:long}", (long runId) =>
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            long id;
            Guid clientRunId;
            DateTime startedAt;
            DateTime completedAt;
            long userId;
            string userFullName;
            string userDepartment;
            string cabinetTitle;
            string qrCode;
            bool hasMedia;
            int photoCount;
            using (var command = new NpgsqlCommand(@"
                SELECT r.id, r.client_run_id, r.started_at, r.completed_at,
                       r.user_id, u.full_name, u.department, c.title, q.code,
                       EXISTS (SELECT 1 FROM inventory_run_media m WHERE m.inventory_run_id = r.id)
                FROM inventory_runs r
                JOIN users u ON u.id = r.user_id
                JOIN cabinet_profiles c ON c.id = r.cabinet_id
                JOIN qr_codes q ON q.id = r.qr_code_id
                WHERE r.id = @runId
            ", connection))
            {
                command.Parameters.AddWithValue("runId", runId);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return Results.NotFound(new { message = "Инвентаризация не найдена" });
                id = reader.GetInt64(0);
                clientRunId = reader.GetGuid(1);
                startedAt = reader.GetDateTime(2);
                completedAt = reader.GetDateTime(3);
                userId = reader.GetInt64(4);
                userFullName = reader.GetString(5);
                userDepartment = reader.GetString(6);
                cabinetTitle = reader.GetString(7);
                qrCode = reader.GetString(8);
                hasMedia = reader.GetBoolean(9);
            }

            using (var command = new NpgsqlCommand(@"SELECT CASE WHEN EXISTS (SELECT 1 FROM inventory_run_media WHERE inventory_run_id=@id) THEN 1 ELSE 0 END + (SELECT COUNT(*) FROM inventory_run_photos WHERE inventory_run_id=@id)", connection))
            {
                command.Parameters.AddWithValue("id", runId);
                photoCount = Convert.ToInt32(command.ExecuteScalar());
            }

            var items = new List<object>();
            using (var command = new NpgsqlCommand(@"
                SELECT runItem.item_name_snapshot, runItem.status, COALESCE(runItem.comment, '')
                FROM inventory_run_items runItem
                JOIN cabinet_checklist_items checklistItem ON checklistItem.id = runItem.checklist_item_id
                WHERE runItem.inventory_run_id = @runId
                ORDER BY checklistItem.sort_order, checklistItem.id, runItem.id
            ", connection))
            {
                command.Parameters.AddWithValue("runId", runId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    items.Add(new { itemName = reader.GetString(0), status = reader.GetString(1), comment = reader.GetString(2) });
            }

            return Results.Ok(new
            {
                id, clientRunId, startedAt, completedAt, userId, userFullName,
                userDepartment, cabinetTitle, qrCode, items,
                photoUrl = hasMedia ? $"/api/admin/inventory/runs/{id}/media/photo" : null,
                photoUrls = Enumerable.Range(0, photoCount).Select(index => $"/api/admin/inventory/runs/{id}/media/photo/{index}").ToArray(),
                voiceUrl = hasMedia ? $"/api/admin/inventory/runs/{id}/media/voice" : null
            });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Подробности выбранной инвентаризации")
        .WithDescription("Показывает выбор сотрудника по каждому пункту чек-листа и его комментарий.")
        .Produces(200)
        .Produces(404);

        endpoints.MapGet("/api/admin/inventory/runs/{runId:long}/media/photo/{index:int}", (long runId, int index, HttpContext context) =>
        {
            if (index < 0 || index > 4) return Results.NotFound();
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = index == 0
                ? new NpgsqlCommand("SELECT photo_jpeg FROM inventory_run_media WHERE inventory_run_id=@id", connection)
                : new NpgsqlCommand("SELECT photo_jpeg FROM inventory_run_photos WHERE inventory_run_id=@id AND photo_index=@index", connection);
            command.Parameters.AddWithValue("id", runId);
            if (index > 0) command.Parameters.AddWithValue("index", index);
            if (command.ExecuteScalar() is not byte[] bytes) return Results.NotFound();
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Bytes(bytes, "image/jpeg", enableRangeProcessing: true);
        }).WithTags("Admin: Inventory").WithSummary("Фотография отчёта инвентаризации");

        endpoints.MapGet("/api/admin/inventory/runs/{runId:long}/media/{kind}", (long runId, string kind, HttpContext context) =>
        {
            const string photoSql = "SELECT photo_jpeg FROM inventory_run_media WHERE inventory_run_id = @runId";
            const string voiceSql = "SELECT voice_wav FROM inventory_run_media WHERE inventory_run_id = @runId";
            if (kind != "photo" && kind != "voice") return Results.NotFound();

            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(kind == "photo" ? photoSql : voiceSql, connection);
            command.Parameters.AddWithValue("runId", runId);
            if (command.ExecuteScalar() is not byte[] bytes) return Results.NotFound();
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Bytes(bytes, kind == "photo" ? "image/jpeg" : "audio/wav", enableRangeProcessing: true);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Получить фото или голосовое сообщение инвентаризации")
        .Produces(200)
        .Produces(404);

        return endpoints;
    }

    public static (long? CabinetId, int DeletedRunItems, int DeletedRuns, int DeletedChecklistItems, int DeletedPhotos, int DeletedEvents)?
        DeleteQrDependencies(NpgsqlConnection connection, NpgsqlTransaction transaction, long qrCodeId, bool requireCabinet)
    {
        long? cabinetId;
        using (var command = new NpgsqlCommand(
            "SELECT id FROM cabinet_profiles WHERE qr_code_id = @qrCodeId", connection, transaction))
        {
            command.Parameters.AddWithValue("qrCodeId", qrCodeId);
            var result = command.ExecuteScalar();
            cabinetId = result is null ? null : Convert.ToInt64(result);
        }

        if (requireCabinet && cabinetId is null)
            return null;

        var cabinetRunFilter = cabinetId.HasValue ? " OR run.cabinet_id = @cabinetId" : string.Empty;
        int deletedRunItems;
        using (var command = new NpgsqlCommand($@"
            DELETE FROM inventory_run_items item
            USING inventory_runs run
            WHERE item.inventory_run_id = run.id
              AND (run.qr_code_id = @qrCodeId{cabinetRunFilter})
        ", connection, transaction))
        {
            command.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (cabinetId.HasValue)
                command.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deletedRunItems = command.ExecuteNonQuery();
        }

        var cabinetDeleteFilter = cabinetId.HasValue ? " OR cabinet_id = @cabinetId" : string.Empty;
        int deletedRuns;
        using (var command = new NpgsqlCommand(
            $"DELETE FROM inventory_runs WHERE qr_code_id = @qrCodeId{cabinetDeleteFilter}", connection, transaction))
        {
            command.Parameters.AddWithValue("qrCodeId", qrCodeId);
            if (cabinetId.HasValue)
                command.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deletedRuns = command.ExecuteNonQuery();
        }

        int deletedChecklistItems = 0;
        int deletedPhotos = 0;
        int deletedEvents = 0;
        if (cabinetId.HasValue)
        {
            using (var command = new NpgsqlCommand(
                "DELETE FROM cabinet_checklist_items WHERE cabinet_id = @cabinetId", connection, transaction))
            {
                command.Parameters.AddWithValue("cabinetId", cabinetId.Value);
                deletedChecklistItems = command.ExecuteNonQuery();
            }

            using (var command = new NpgsqlCommand(
                "DELETE FROM cabinet_photos WHERE cabinet_id = @cabinetId", connection, transaction))
            {
                command.Parameters.AddWithValue("cabinetId", cabinetId.Value);
                deletedPhotos = command.ExecuteNonQuery();
            }

            using (var command = new NpgsqlCommand(
                "DELETE FROM inventory_sessions WHERE qr_code_id = @qrCodeId", connection, transaction))
            {
                command.Parameters.AddWithValue("qrCodeId", qrCodeId);
                deletedEvents = command.ExecuteNonQuery();
            }

            using var deleteCabinet = new NpgsqlCommand(
                "DELETE FROM cabinet_profiles WHERE id = @cabinetId", connection, transaction);
            deleteCabinet.Parameters.AddWithValue("cabinetId", cabinetId.Value);
            deleteCabinet.ExecuteNonQuery();
        }

        return (cabinetId, deletedRunItems, deletedRuns, deletedChecklistItems, deletedPhotos, deletedEvents);
    }

    private static SaveRunResult SaveRun(NpgsqlConnection connection, InventoryRunSyncItem run)
    {
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

        var media = ReadReportMedia(run);
        using var transaction = connection.BeginTransaction();

        using (var existingCommand = new NpgsqlCommand(
                   "SELECT id FROM inventory_runs WHERE client_run_id = @clientRunId",
                   connection, transaction))
        {
            existingCommand.Parameters.AddWithValue("clientRunId", run.clientRunId);
            if (existingCommand.ExecuteScalar() is not null)
            {
                transaction.Rollback();
                return SaveRunResult.Duplicate;
            }
        }

        using (var userCommand = new NpgsqlCommand(
                   "SELECT 1 FROM users WHERE id = @userId AND is_active = TRUE",
                   connection, transaction))
        {
            userCommand.Parameters.AddWithValue("userId", run.userId);
            if (userCommand.ExecuteScalar() is null)
                throw new ArgumentException("Проверка пропущена: сотрудник не найден или отключён.");
        }

        long cabinetId;
        long qrCodeId;
        string cabinetTitle;
        using (var cabinetCommand = new NpgsqlCommand(@"
            SELECT c.id, q.id, c.title
            FROM cabinet_profiles c
            JOIN qr_codes q ON q.id = c.qr_code_id
            WHERE q.code = ANY(@codes) AND q.is_active = TRUE AND c.is_active = TRUE
            LIMIT 1
        ", connection, transaction))
        {
            cabinetCommand.Parameters.AddWithValue("codes", ExpandQr(run.qrCode).ToArray());
            using var reader = cabinetCommand.ExecuteReader();
            if (!reader.Read())
                throw new ArgumentException("Проверка пропущена: шкаф для QR не найден.");
            cabinetId = reader.GetInt64(0);
            qrCodeId = reader.GetInt64(1);
            cabinetTitle = reader.GetString(2);
        }

        var requestedIds = run.items.Select(item => item.itemId).ToArray();
        var itemNames = new Dictionary<long, string>();
        using (var itemCommand = new NpgsqlCommand(@"
            SELECT id, item_name
            FROM cabinet_checklist_items
            WHERE cabinet_id = @cabinetId AND is_active = TRUE AND id = ANY(@itemIds)
        ", connection, transaction))
        {
            itemCommand.Parameters.AddWithValue("cabinetId", cabinetId);
            itemCommand.Parameters.AddWithValue("itemIds", requestedIds);
            using var reader = itemCommand.ExecuteReader();
            while (reader.Read()) itemNames[reader.GetInt64(0)] = reader.GetString(1);
        }

        if (itemNames.Count != requestedIds.Length)
            throw new ArgumentException("Проверка пропущена: перечень шкафа изменился, обновите данные в приложении.");

        using (var countCommand = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM cabinet_checklist_items
            WHERE cabinet_id = @cabinetId AND is_active = TRUE
        ", connection, transaction))
        {
            countCommand.Parameters.AddWithValue("cabinetId", cabinetId);
            if (Convert.ToInt32(countCommand.ExecuteScalar()) != requestedIds.Length)
                throw new ArgumentException("Проверка пропущена: в сохранённом перечне не все предметы.");
        }

        var startedAt = ParseTime(run.startedAt, DateTimeOffset.UtcNow);
        var completedAt = ParseTime(run.completedAt, DateTimeOffset.UtcNow);
        if (completedAt < startedAt)
            throw new ArgumentException("Проверка пропущена: время завершения раньше времени начала.");

        long inventoryRunId;
        using (var insertRun = new NpgsqlCommand(@"
            INSERT INTO inventory_runs
                (client_run_id, cabinet_id, user_id, qr_code_id, started_at, completed_at)
            VALUES (@clientRunId, @cabinetId, @userId, @qrCodeId, @startedAt, @completedAt)
            ON CONFLICT (client_run_id) DO NOTHING
            RETURNING id
        ", connection, transaction))
        {
            insertRun.Parameters.AddWithValue("clientRunId", run.clientRunId);
            insertRun.Parameters.AddWithValue("cabinetId", cabinetId);
            insertRun.Parameters.AddWithValue("userId", (int)run.userId);
            insertRun.Parameters.AddWithValue("qrCodeId", qrCodeId);
            insertRun.Parameters.AddWithValue("startedAt", startedAt.UtcDateTime);
            insertRun.Parameters.AddWithValue("completedAt", completedAt.UtcDateTime);
            if (insertRun.ExecuteScalar() is not long insertedId)
                return SaveRunResult.Duplicate;
            inventoryRunId = insertedId;
        }

        if (media is { } attachments)
        {
            using var insertMedia = new NpgsqlCommand(@"
                INSERT INTO inventory_run_media (inventory_run_id, photo_jpeg, voice_wav)
                VALUES (@runId, @photo, @voice)
            ", connection, transaction);
            insertMedia.Parameters.AddWithValue("runId", inventoryRunId);
            insertMedia.Parameters.AddWithValue("photo", NpgsqlDbType.Bytea, attachments.Photos[0]);
            insertMedia.Parameters.AddWithValue("voice", NpgsqlDbType.Bytea, attachments.Voice);
            insertMedia.ExecuteNonQuery();
            for (int photoIndex = 1; photoIndex < attachments.Photos.Count; photoIndex++)
            {
                using var insertPhoto = new NpgsqlCommand(@"INSERT INTO inventory_run_photos (inventory_run_id, photo_index, photo_jpeg) VALUES (@runId, @photoIndex, @photo)", connection, transaction);
                insertPhoto.Parameters.AddWithValue("runId", inventoryRunId);
                insertPhoto.Parameters.AddWithValue("photoIndex", photoIndex);
                insertPhoto.Parameters.Add("photo", NpgsqlDbType.Bytea).Value = attachments.Photos[photoIndex];
                insertPhoto.ExecuteNonQuery();
            }
        }

        foreach (var item in run.items)
        {
            using var insertItem = new NpgsqlCommand(@"
                INSERT INTO inventory_run_items
                    (inventory_run_id, checklist_item_id, item_name_snapshot, status, comment)
                VALUES (@runId, @itemId, @itemName, @status, @comment)
            ", connection, transaction);
            insertItem.Parameters.AddWithValue("runId", inventoryRunId);
            insertItem.Parameters.AddWithValue("itemId", item.itemId);
            insertItem.Parameters.AddWithValue("itemName", itemNames[item.itemId]);
            insertItem.Parameters.AddWithValue("status", item.status);
            insertItem.Parameters.AddWithValue("comment", string.IsNullOrWhiteSpace(item.comment)
                ? (object)DBNull.Value
                : item.comment.Trim());
            insertItem.ExecuteNonQuery();
        }

        using (var logCommand = new NpgsqlCommand(@"
            INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment, action_time)
            VALUES (@userId, @qrCodeId, 'inventory', @comment, @completedAt)
        ", connection, transaction))
        {
            int present = run.items.Count(item => item.status == "present");
            int missing = run.items.Count - present;
            logCommand.Parameters.AddWithValue("userId", run.userId);
            logCommand.Parameters.AddWithValue("qrCodeId", qrCodeId);
            logCommand.Parameters.AddWithValue("comment", $"Инвентаризация «{cabinetTitle}»: есть {present}, нет {missing}");
            logCommand.Parameters.AddWithValue("completedAt", completedAt.UtcDateTime);
            logCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return SaveRunResult.Imported;
    }

    private static (List<byte[]> Photos, byte[] Voice)? ReadReportMedia(InventoryRunSyncItem run)
    {
        var encodedPhotos = run.photosBase64 is { Count: > 0 } ? run.photosBase64 :
            string.IsNullOrEmpty(run.photoBase64) ? new List<string>() : new List<string> { run.photoBase64 };
        if (encodedPhotos.Count > 5) throw new ArgumentException("В отчёте допускается не более 5 фотографий.");
        if (encodedPhotos.Count == 0 && string.IsNullOrEmpty(run.voiceBase64))
            return null; // Reports queued by older APKs contain only the checklist.

        var photos = encodedPhotos.Select(encoded => DecodeAttachment(encoded, MaxReportPhotoBytes, "фото")).ToList();
        if (photos.Sum(photo => (long)photo.Length) > 12L * 1024 * 1024)
            throw new ArgumentException("Общий размер фотографий в отчёте не должен превышать 12 МБ.");
        if (photos.Any(photo => photo.Length < 4 || photo[0] != 0xff || photo[1] != 0xd8 || photo[^2] != 0xff || photo[^1] != 0xd9))
            throw new ArgumentException("Проверка пропущена: ожидается JPEG для каждой фотографии.");
        byte[] voice = DecodeAttachment(run.voiceBase64, MaxReportVoiceBytes, "голосовое сообщение");
        if (voice.Length < 46 || !voice.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !voice.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) ||
            !voice.AsSpan(36, 4).SequenceEqual("data"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(4)) != voice.Length - 8 ||
            BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(16)) != 16 ||
            BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(20)) != 1 ||
            BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(22)) != 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(24)) != 16000 ||
            BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(28)) != 32000 ||
            BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(32)) != 2 ||
            BinaryPrimitives.ReadInt16LittleEndian(voice.AsSpan(34)) != 16 ||
            BinaryPrimitives.ReadInt32LittleEndian(voice.AsSpan(40)) != voice.Length - 44 ||
            voice.Length % 2 != 0)
            throw new ArgumentException("Проверка пропущена: ожидается голосовое сообщение WAV.");
        return (photos, voice);
    }

    private static byte[] DecodeAttachment(string? encoded, int limit, string name)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > ((limit + 2) / 3) * 4)
            throw new ArgumentException($"Проверка пропущена: отсутствует или превышает допустимый размер вложение «{name}».");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new ArgumentException($"Проверка пропущена: повреждено вложение «{name}»."); }
        if (bytes.Length == 0 || bytes.Length > limit)
            throw new ArgumentException($"Проверка пропущена: недопустимый размер вложения «{name}».");
        return bytes;
    }

    private static IEnumerable<string> ExpandQr(string value)
    {
        var trimmed = value.Trim();
        yield return trimmed;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) yield break;

        var tail = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "");
        if (!string.IsNullOrWhiteSpace(tail) && !string.Equals(tail, trimmed, StringComparison.Ordinal))
            yield return tail;
    }

    private static DateTimeOffset ParseTime(string? value, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : fallback;

    private enum SaveRunResult { Imported, Duplicate }
}

public sealed class AdminCabinetDto
{
    public string title { get; set; } = string.Empty;
    public string? photoBase64 { get; set; }
    public string? photoContentType { get; set; }
    public List<AdminCabinetPhotoDto>? photos { get; set; }
    public List<int>? removePhotoPages { get; set; }
    public List<AdminChecklistItemDto> items { get; set; } = new();
}

public sealed class AdminCabinetPhotoDto
{
    public int pageNumber { get; set; }
    public string? caption { get; set; }
    public string? imageBase64 { get; set; }
    public string? contentType { get; set; }
}

public sealed class AdminChecklistItemDto
{
    public long id { get; set; }
    public string? name { get; set; }
    public string? description { get; set; }
    public int sortOrder { get; set; }
}

public sealed class InventoryRunSyncPayload
{
    public List<InventoryRunSyncItem> items { get; set; } = new();
}

public sealed class InventoryRunSyncItem
{
    public Guid clientRunId { get; set; }
    public long userId { get; set; }
    public string qrCode { get; set; } = string.Empty;
    public string startedAt { get; set; } = string.Empty;
    public string completedAt { get; set; } = string.Empty;
    public List<InventoryRunSyncResult> items { get; set; } = new();
    public string? photoBase64 { get; set; }
    public List<string>? photosBase64 { get; set; }
    public string? voiceBase64 { get; set; }
}

public sealed class InventoryRunSyncResult
{
    public long itemId { get; set; }
    public string status { get; set; } = string.Empty;
    public string? comment { get; set; }
}

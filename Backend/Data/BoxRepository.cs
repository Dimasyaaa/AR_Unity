using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace Backend.Data;

/// <summary>Читает и сохраняет коробки, их содержимое, фотографии и 3D-модели.</summary>
public sealed class BoxRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public BoxRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Находит активную коробку по QR-коду или его последнему сегменту.</summary>
    public async Task<(long boxId, long qrCodeId, string title, string code, bool hasPhoto, string contentType)?>
        FindByQrCodeAsync(string qrCode)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT box.id, qr.id, box.title, qr.code,
                   EXISTS (SELECT 1 FROM box_photos photo WHERE photo.box_id = box.id),
                   COALESCE((SELECT photo.content_type FROM box_photos photo
                             WHERE photo.box_id = box.id ORDER BY photo.page_number LIMIT 1), 'image/jpeg')
            FROM boxes box
            JOIN qr_codes qr ON qr.id = box.qr_code_id
            WHERE qr.code = ANY(@codes) AND qr.is_active = TRUE AND box.is_active = TRUE
            LIMIT 1", connection);

        var codes = command.Parameters.AddWithValue("codes", Helpers.QrHelper.ExpandQr(qrCode).ToArray());
        codes.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
            reader.GetBoolean(4), reader.GetString(5));
    }

    /// <summary>Возвращает активные строки перечня содержимого коробки.</summary>
    public async Task<List<object>> GetChecklistItemsAsync(long boxId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT id, name, COALESCE(description, ''), sort_order, expected_quantity, model_type
            FROM box_items
            WHERE box_id = @boxId AND is_active = TRUE
            ORDER BY sort_order, id", connection);
        command.Parameters.AddWithValue("boxId", boxId);

        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                name = reader.GetString(1),
                description = reader.GetString(2),
                sortOrder = reader.GetInt32(3),
                expectedQuantity = reader.GetInt32(4),
                modelType = reader.GetString(5)
            });
        }
        return items;
    }

    /// <summary>Возвращает метаданные всех изображений содержимого коробки.</summary>
    public async Task<List<object>> GetBoxPhotosAsync(long boxId, string code)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT page_number, caption, content_type
            FROM box_photos
            WHERE box_id = @boxId
            ORDER BY page_number", connection);
        command.Parameters.AddWithValue("boxId", boxId);

        var photos = new List<object>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int page = reader.GetInt32(0);
            photos.Add(new
            {
                pageNumber = page,
                caption = reader.GetString(1),
                contentType = reader.GetString(2),
                photoUrl = $"/api/inventory/box-photo?qrCode={Uri.EscapeDataString(code)}&pageNumber={page}"
            });
        }
        return photos;
    }

    /// <summary>Возвращает файл фотографии по QR-коду и номеру страницы.</summary>
    public async Task<(byte[] bytes, string contentType)?> GetBoxPhotoAsync(string qrCode, int pageNumber)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT photo.image_data, photo.content_type
            FROM boxes box
            JOIN qr_codes qr ON qr.id = box.qr_code_id
            JOIN box_photos photo ON photo.box_id = box.id AND photo.page_number = @page
            WHERE qr.code = ANY(@codes) AND qr.is_active = TRUE AND box.is_active = TRUE
            LIMIT 1", connection);

        var codes = command.Parameters.AddWithValue("codes", Helpers.QrHelper.ExpandQr(qrCode).ToArray());
        codes.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
        command.Parameters.AddWithValue("page", pageNumber);

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync()) return null;
        return ((byte[])reader[0], reader.GetString(1));
    }

    /// <summary>Показывает, загружена ли для коробки GLB-модель.</summary>
    public async Task<bool> HasBoxModelAsync(long boxId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT model_data IS NOT NULL FROM boxes WHERE id = @boxId", connection);
        command.Parameters.AddWithValue("boxId", boxId);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    /// <summary>Возвращает GLB-файл коробки по её QR-коду.</summary>
    public async Task<(byte[] data, string contentType, string fileName)?> GetBoxModelAsync(string qrCode)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT box.model_data, box.model_content_type, box.model_file_name
            FROM boxes box
            JOIN qr_codes qr ON qr.id = box.qr_code_id
            WHERE qr.code = ANY(@codes) AND qr.is_active = TRUE AND box.is_active = TRUE
              AND box.model_data IS NOT NULL
            LIMIT 1", connection);
        var codes = command.Parameters.AddWithValue("codes", Helpers.QrHelper.ExpandQr(qrCode).ToArray());
        codes.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync()) return null;
        return ((byte[])reader[0], reader.GetString(1), reader.GetString(2));
    }

    /// <summary>Возвращает список QR-кодов и краткие сведения о связанных коробках.</summary>
    public async Task<List<object>> GetAllBoxesAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT qr.id, qr.code, qr.object_name, box.id, box.title, qr.is_active,
                   EXISTS (SELECT 1 FROM box_photos photo WHERE photo.box_id = box.id),
                   (SELECT COUNT(*) FROM box_items item WHERE item.box_id = box.id AND item.is_active = TRUE),
                   box.model_data IS NOT NULL
            FROM qr_codes qr
            LEFT JOIN boxes box ON box.qr_code_id = qr.id
            ORDER BY qr.object_name, qr.id", connection);

        var boxes = new List<object>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            boxes.Add(new
            {
                qrCodeId = reader.GetInt64(0),
                qrCode = reader.GetString(1),
                objectName = reader.GetString(2),
                boxId = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3),
                title = reader.IsDBNull(4) ? reader.GetString(2) : reader.GetString(4),
                isActive = reader.GetBoolean(5),
                photoAvailable = reader.GetBoolean(6),
                itemCount = reader.GetInt64(7),
                modelAvailable = !reader.IsDBNull(8) && reader.GetBoolean(8)
            });
        }
        return boxes;
    }

    /// <summary>Возвращает коробку, найденную по идентификатору её QR-кода.</summary>
    public async Task<(long boxId, string code, string objectName, string title,
        bool photoAvailable, string? modelFileName)?> GetBoxByQrCodeIdAsync(long qrCodeId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(@"
            SELECT qr.code, qr.object_name, box.id, box.title,
                   EXISTS (SELECT 1 FROM box_photos photo WHERE photo.box_id = box.id),
                   box.model_file_name
            FROM qr_codes qr
            LEFT JOIN boxes box ON box.qr_code_id = qr.id
            WHERE qr.id = @qrCodeId", connection);
        command.Parameters.AddWithValue("qrCodeId", qrCodeId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.IsDBNull(2) ? 0 : reader.GetInt64(2), reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(3) ? reader.GetString(1) : reader.GetString(3),
            reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    /// <summary>Сохраняет карточку коробки, перечень, изображения и необязательную GLB-модель.</summary>
    public async Task<long?> UpsertBoxAsync(
        long qrCodeId,
        string title,
        List<AdminChecklistItemDto> items,
        List<(int page, byte[] bytes, string type, string caption)> photoUploads,
        int[] removalPages,
        byte[]? glbModel = null,
        string? modelFileName = null,
        bool removeModel = false)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var upsertBox = new NpgsqlCommand(@"
            INSERT INTO boxes (qr_code_id, title, is_active)
            SELECT qr.id, @title, TRUE FROM qr_codes qr WHERE qr.id = @qrCodeId
            ON CONFLICT (qr_code_id) DO UPDATE SET
                title = EXCLUDED.title,
                is_active = TRUE,
                updated_at = NOW()
            RETURNING id", connection, transaction);
        upsertBox.Parameters.AddWithValue("qrCodeId", qrCodeId);
        upsertBox.Parameters.AddWithValue("title", title);

        var result = await upsertBox.ExecuteScalarAsync();
        if (result is null)
        {
            await transaction.RollbackAsync();
            return null;
        }
        long boxId = Convert.ToInt64(result);

        foreach (int page in removalPages)
        {
            await using var removePhoto = new NpgsqlCommand(
                "DELETE FROM box_photos WHERE box_id = @boxId AND page_number = @page", connection, transaction);
            removePhoto.Parameters.AddWithValue("boxId", boxId);
            removePhoto.Parameters.AddWithValue("page", page);
            await removePhoto.ExecuteNonQueryAsync();
        }

        foreach (var upload in photoUploads)
        {
            await using var savePhoto = new NpgsqlCommand(@"
                INSERT INTO box_photos (box_id, page_number, image_data, content_type, caption, updated_at)
                VALUES (@boxId, @page, @image, @type, @caption, NOW())
                ON CONFLICT (box_id, page_number) DO UPDATE SET
                    image_data = EXCLUDED.image_data,
                    content_type = EXCLUDED.content_type,
                    caption = EXCLUDED.caption,
                    updated_at = NOW()", connection, transaction);
            savePhoto.Parameters.AddWithValue("boxId", boxId);
            savePhoto.Parameters.AddWithValue("page", upload.page);
            savePhoto.Parameters.Add("image", NpgsqlDbType.Bytea).Value = upload.bytes;
            savePhoto.Parameters.AddWithValue("type", upload.type);
            savePhoto.Parameters.AddWithValue("caption", upload.caption);
            await savePhoto.ExecuteNonQueryAsync();
        }

        await using (var archiveItems = new NpgsqlCommand(@"
            UPDATE box_items
            SET is_active = FALSE, name = '__inventory_archived_' || id::text || '_' || txid_current()::text
            WHERE box_id = @boxId AND is_active = TRUE", connection, transaction))
        {
            archiveItems.Parameters.AddWithValue("boxId", boxId);
            await archiveItems.ExecuteNonQueryAsync();
        }

        foreach (var item in items)
        {
            if (item.id > 0)
            {
                await using var updateItem = new NpgsqlCommand(@"
                    UPDATE box_items
                    SET name = @name, description = @description, sort_order = @sortOrder,
                        expected_quantity = @expectedQuantity, model_type = @modelType, is_active = TRUE
                    WHERE id = @id AND box_id = @boxId", connection, transaction);
                updateItem.Parameters.AddWithValue("id", item.id);
                updateItem.Parameters.AddWithValue("boxId", boxId);
                AddChecklistParameters(updateItem, item);
                if (await updateItem.ExecuteNonQueryAsync() == 0)
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException("Позиция перечня не относится к выбранной коробке.");
                }
            }
            else
            {
                await using var insertItem = new NpgsqlCommand(@"
                    INSERT INTO box_items
                        (box_id, name, description, sort_order, is_active, expected_quantity, model_type)
                    VALUES (@boxId, @name, @description, @sortOrder, TRUE, @expectedQuantity, @modelType)
                    ON CONFLICT (box_id, name) DO UPDATE SET
                        description = EXCLUDED.description,
                        sort_order = EXCLUDED.sort_order,
                        expected_quantity = EXCLUDED.expected_quantity,
                        model_type = EXCLUDED.model_type,
                        is_active = TRUE", connection, transaction);
                insertItem.Parameters.AddWithValue("boxId", boxId);
                AddChecklistParameters(insertItem, item);
                await insertItem.ExecuteNonQueryAsync();
            }
        }

        if (removeModel || glbModel is { Length: > 0 })
        {
            await using var saveModel = new NpgsqlCommand(@"
                UPDATE boxes
                SET model_file_name = @fileName,
                    model_content_type = @contentType,
                    model_data = @modelData,
                    updated_at = NOW()
                WHERE id = @boxId", connection, transaction);
            bool hasModel = !removeModel && glbModel is { Length: > 0 };
            saveModel.Parameters.AddWithValue("boxId", boxId);
            saveModel.Parameters.Add("fileName", NpgsqlDbType.Text).Value = hasModel
                ? (string.IsNullOrWhiteSpace(modelFileName) ? "box-content.glb" : modelFileName)
                : DBNull.Value;
            saveModel.Parameters.Add("contentType", NpgsqlDbType.Text).Value = hasModel
                ? "model/gltf-binary"
                : DBNull.Value;
            saveModel.Parameters.Add("modelData", NpgsqlDbType.Bytea).Value = hasModel
                ? glbModel!
                : DBNull.Value;
            await saveModel.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return boxId;
    }

    private static void AddChecklistParameters(NpgsqlCommand command, AdminChecklistItemDto item)
    {
        command.Parameters.AddWithValue("name", item.name!);
        command.Parameters.Add("description", NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(item.description) ? DBNull.Value : item.description!;
        command.Parameters.AddWithValue("sortOrder", item.sortOrder);
        command.Parameters.AddWithValue("expectedQuantity", Math.Max(1, item.expectedQuantity));
        command.Parameters.AddWithValue("modelType", NormalizeModelType(item.modelType));
    }

    /// <summary>Оставляет только поддерживаемые типы тестовых фигур.</summary>
    private static string NormalizeModelType(string? modelType) => modelType switch
    {
        "sphere" or "cylinder" or "capsule" or "cone" => modelType,
        _ => "cube"
    };
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backend.Contracts;   // QrDto
using Npgsql;

namespace Backend.Data;

/// <summary>
/// Слой доступа к таблице "qr_codes".
/// SQL (GET/POST/PUT /api/admin/qr) и из /api/scans.
/// Удаление QR со связанными данными (DELETE) вынесено отдельно в QrDeletionRepository,
/// потому что там транзакция и каскад по таблицам инвентаря.
/// </summary>
public class QrRepository
{
    private readonly NpgsqlDataSource _dataSource;
    public QrRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Все QR-коды для админки (GET /api/admin/qr), сортировка по object_name.
    /// </summary>
    public async Task<List<object>> GetAllQrCodesAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, code, object_name, is_active, created_at
            FROM qr_codes
            ORDER BY object_name", conn);

        var items = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt64(0),
                code = reader.GetString(1),
                objectName = reader.GetString(2),
                isActive = reader.GetBoolean(3),
                createdAt = reader.GetDateTime(4)
            });
        }
        return items;
    }

    /// <summary>
    /// Активный QR по значению кода (используется в POST /api/scans).
    /// Возвращает (id, objectName) либо null, если не найден/не активен.
    /// </summary>
    public async Task<(long id, string objectName)?> GetQrByCodeAsync(string code)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, object_name
            FROM qr_codes
            WHERE code = @code AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("@code", code);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.GetInt64(0), reader.GetString(1));
    }

    /// <summary>
    /// Занят ли уже такой код (проверка уникальности перед POST /api/admin/qr).
    /// </summary>
    public async Task<bool> ExistsByCodeAsync(string code)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM qr_codes WHERE code = @code", conn);
        cmd.Parameters.AddWithValue("@code", code);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Занят ли код у ДРУГОЙ записи (проверка перед PUT /api/admin/qr/{id}).
    /// </summary>
    public async Task<bool> ExistsByCodeExceptAsync(long id, string code)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM qr_codes WHERE id != @id AND code = @code", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@code", code);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Создание QR (POST /api/admin/qr). Возвращает новый id.
    /// </summary>
    public async Task<long> CreateQrAsync(QrDto dto)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO qr_codes (code, object_name, is_active)
            VALUES (@code, @obj, @active)
            RETURNING id", conn);
        cmd.Parameters.AddWithValue("@code", dto.code.Trim());
        cmd.Parameters.AddWithValue("@obj", dto.objectName.Trim());
        cmd.Parameters.AddWithValue("@active", dto.isActive);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Обновление QR (PUT /api/admin/qr/{id}). true = найдена и обновлена запись.
    /// </summary>
    public async Task<bool> UpdateQrAsync(long id, QrDto dto)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE qr_codes SET code = @code, object_name = @obj, is_active = @active
            WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@code", dto.code.Trim());
        cmd.Parameters.AddWithValue("@obj", dto.objectName.Trim());
        cmd.Parameters.AddWithValue("@active", dto.isActive);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Удаление QR-кода по ID (используется в DELETE /api/admin/qr/{id}).
    /// Возвращает true, если запись была удалена.
    /// </summary>
    public async Task<bool> DeleteByIdAsync(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM qr_codes WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Проверка существования QR по ID (для DELETE /api/admin/qr/{id}).
    /// </summary>
    public async Task<bool> ExistsByIdAsync(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM qr_codes WHERE id = @id)", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
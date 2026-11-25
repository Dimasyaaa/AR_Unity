using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backend.Contracts; // SyncSessionDto
using Npgsql;

namespace Backend.Data;

/// <summary>
/// Слой доступа к таблице "inventory_sessions" (журнал событий).
/// Сюда перенесена логика из эндпоинтов:
/// - POST /api/auth/login (запись входа)
/// - POST /api/scans (запись сканирования и проверка дубликатов)
/// - GET/DELETE /api/admin/sessions (просмотр и очистка журнала)
/// - POST /api/sync/sessions (офлайн-синхронизация с телефона)
/// </summary>
public class SessionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public SessionRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Запись события в журнал (используется при логине и сканировании).
    /// </summary>
    public async Task<long> LogSessionAsync(long userId, string action, string comment, long? qrCodeId = null)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment)
            VALUES (@userId, @qrId, @action, @comment)
            RETURNING id", conn);

        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@qrId", qrCodeId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@comment", comment);

        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Проверка, сканировал ли уже этот пользователь этот конкретный QR-код.
    /// Используется в POST /api/scans для пометки "duplicate".
    /// </summary>
    public async Task<bool> HasScanSessionAsync(long userId, long qrCodeId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM inventory_sessions 
            WHERE user_id = @userId AND qr_code_id = @qrCodeId AND action = 'scan'", conn);
        
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@qrCodeId", qrCodeId);

        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Получение последних 500 записей журнала для админки (GET /api/admin/sessions).
    /// Включает JOIN с users и LEFT JOIN с qr_codes для отображения имен, а не только ID.
    /// </summary>
    public async Task<List<object>> GetSessionsAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                s.id, s.action, s.action_time, s.comment, 
                COALESCE(u.full_name, 'Общий терминал'), COALESCE(u.department, ''),
                q.code, q.object_name
            FROM inventory_sessions s
            LEFT JOIN users u ON u.id = s.user_id
            LEFT JOIN qr_codes q ON q.id = s.qr_code_id
            ORDER BY s.action_time DESC
            LIMIT 500", conn);

        var sessions = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new
            {
                id = reader.GetInt64(0),
                action = reader.GetString(1),
                actionTime = reader.GetDateTime(2),
                comment = reader.IsDBNull(3) ? null : reader.GetString(3),
                userFullName = reader.GetString(4),
                userDepartment = reader.GetString(5),
                qrCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                qrObjectName = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }
        return sessions;
    }

    /// <summary>
    /// Удаление одной записи журнала по ID (DELETE /api/admin/sessions/{id}).
    /// </summary>
    public async Task<bool> DeleteSessionAsync(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM inventory_sessions WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Полная очистка журнала (DELETE /api/admin/sessions).
    /// Возвращает количество удаленных строк.
    /// </summary>
    public async Task<int> DeleteAllSessionsAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM inventory_sessions", conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Офлайн-синхронизация записей с мобильного приложения (POST /api/sync/sessions).
    /// Логика 1-в-1 из оригинала:
    /// 1. Пытаемся найти пользователя по ФИО+Отдел (надежнее при смене устройств).
    /// 2. Если не нашли, пытаемся найти по userId (запасной вариант).
    /// 3. Если пользователя нет, пропускаем запись (skipped).
    /// 4. Проверяем существование qrCodeId, если его нет в БД, сохраняем как NULL.
    /// 5. Используем INSERT ... WHERE NOT EXISTS для идемпотентности (защита от дублей при повторной отправке).
    /// </summary>
    public async Task<(int imported, int skipped)> SyncSessionsAsync(List<SyncSessionDto> items)
    {
        int imported = 0;
        int skipped =  0; // Изначально 0, увеличиваем при пропуске

        await using var conn = await _dataSource.OpenConnectionAsync();

        foreach (var s in items)
        {
            long userId = 0;

            // 1) Ищем по ФИО + отдел
            if (!string.IsNullOrWhiteSpace(s.fullName) && !string.IsNullOrWhiteSpace(s.department))
            {
                await using var ucmd = new NpgsqlCommand(@"
                    SELECT id FROM users
                    WHERE lower(full_name) = lower(@fn) AND lower(department) = lower(@dep)", conn);
                ucmd.Parameters.AddWithValue("@fn", s.fullName.Trim());
                ucmd.Parameters.AddWithValue("@dep", s.department.Trim());
                
                var r = await ucmd.ExecuteScalarAsync();
                if (r != null) userId = Convert.ToInt64(r);
            }

            // 2) Запасной вариант — по id
            if (userId == 0)
            {
                await using var ucmd2 = new NpgsqlCommand("SELECT id FROM users WHERE id = @id", conn);
                ucmd2.Parameters.AddWithValue("@id", s.userId);
                var r2 = await ucmd2.ExecuteScalarAsync();
                
                // Должна быть только эта строка:
                if (r2 != null) 
                    userId = Convert.ToInt64(r2);
            }

            // Если пользователя так и не нашли, пропускаем эту запись
            if (userId == 0) 
            { 
                skipped++; 
                continue; 
            }

            // QR: если есть в базе — привязываем, иначе NULL
            object qrParam = DBNull.Value;
            if (s.qrCodeId > 0)
            {
                await using var qcmd = new NpgsqlCommand("SELECT id FROM qr_codes WHERE id = @id", conn);
                qcmd.Parameters.AddWithValue("@id", s.qrCodeId);
                if (await qcmd.ExecuteScalarAsync() != null) 
                    qrParam = s.qrCodeId;
            }

            // Разбираем время из ISO-строки; если не вышло — берём текущее UTC
            DateTime time = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(s.actionTime) &&
                DateTime.TryParse(s.actionTime, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                time = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            // Вставка с защитой от дубликатов (идемпотентность)
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment, action_time)
                SELECT @userId, @qrId, @action, @comment, @time
                WHERE NOT EXISTS (
                    SELECT 1 FROM inventory_sessions existing
                    WHERE existing.user_id = @userId
                      AND existing.qr_code_id IS NOT DISTINCT FROM @qrId
                      AND existing.action = @action
                      AND existing.comment IS NOT DISTINCT FROM @comment
                      AND existing.action_time = @time
                )", conn);
            
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@qrId", qrParam);
            cmd.Parameters.AddWithValue("@action", s.action);
            cmd.Parameters.AddWithValue("@comment", string.IsNullOrEmpty(s.comment) ? (object)DBNull.Value : s.comment);
            cmd.Parameters.AddWithValue("@time", time);
            
            if (await cmd.ExecuteNonQueryAsync() > 0) 
                imported++;
            else 
                skipped++; // Запись уже существовала, считаем как пропущенную (или можно считать дубликатом, как в оригинале)
        }

        return (imported, skipped);
    }
}

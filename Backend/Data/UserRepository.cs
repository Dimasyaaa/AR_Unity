using System;                      // Guid, Convert, StringComparison
using System.Collections.Generic;  // List<>
using System.Threading.Tasks;      // Task, async/await
using Backend.Contracts;           // UserDto
using Npgsql;                      // NpgsqlDataSource / NpgsqlCommand

namespace Backend.Data;

/// <summary>
/// Слой доступа к таблице "users".
/// ВЕСЬ SQL
/// MapGet/MapPost/MapPut/MapDelete(эндпоинты /api/departments,
/// /api/admin/departments, /api/auth/login, /api/scans, /api/admin/users...).
/// Эндпоинты теперь только вызывают методы здесь — запросов к БД в маршрутах нет.
/// </summary>
public class UserRepository
{
    // NpgsqlDataSource — современный пул соединений: один на всё приложение,
    // регистрируется в DI как Singleton.
    // OpenConnectionAsync берёт готовое соединение из пула — быстрее, чем new NpgsqlConnection каждый раз.
    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Активные отделы для экрана логина в Unity (эндпоинт GET /api/departments).
    /// SQL перенесён из Program.cs: WHERE is_active = TRUE.
    /// </summary>
    public async Task<List<string>> GetActiveDepartmentsAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT department
            FROM users
            WHERE is_active = TRUE
            ORDER BY department", conn);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>
    /// Все отделы для выпадающего списка в админке (GET /api/admin/departments).
    /// Без фильтра по активности — SQL 1-в-1 из Program.cs.
    /// </summary>
    public async Task<List<string>> GetAllDepartmentsAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT department
            FROM users
            ORDER BY department", conn);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>
    /// Авторизация (POST /api/auth/login).
    /// ФИО и отдел сравниваются без регистра и знаков препинания (regexp_replace),
    /// пароль проверяет pgcrypto через crypt(). Возвращает null, если совпадений нет.
    /// </summary>
    public async Task<(long id, string fullName, string department)?> LoginAsync(
        string fullName, string department, string password)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, full_name, department
            FROM users
            WHERE is_active = TRUE
              AND lower(department) = lower(@department)
              AND regexp_replace(lower(full_name), '[^a-zа-яё]', '', 'g')
                = regexp_replace(lower(@fullName), '[^a-zа-яё]', '', 'g')
              AND password_hash = crypt(@password, password_hash)", conn);

        cmd.Parameters.AddWithValue("@department", department);
        cmd.Parameters.AddWithValue("@fullName", fullName);
        cmd.Parameters.AddWithValue("@password", password);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    /// <summary>
    /// Проверка, что пользователь существует и активен (используется в POST /api/scans).
    /// </summary>
    public async Task<bool> UserExistsAsync(long userId)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM users WHERE id = @id AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("@id", userId);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Список всех пользователей для админки (GET /api/admin/users), включая неактивных.
    /// Поля и порядок сортировки — как в оригинале.
    /// </summary>
    public async Task<List<object>> GetAllUsersAsync()
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, full_name, department, is_active, created_at
            FROM users
            ORDER BY department, full_name", conn);

        var users = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new
            {
                id = reader.GetInt64(0),
                fullName = reader.GetString(1),
                department = reader.GetString(2),
                isActive = reader.GetBoolean(3),
                createdAt = reader.GetDateTime(4)
            });
        }
        return users;
    }

    /// <summary>
    /// Конфликт уникальности ФИО+отдел (проверка перед POST /api/admin/users).
    /// </summary>
    public async Task<bool> ExistsByFullNameAndDeptAsync(string department, string fullName)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM users
            WHERE lower(department) = lower(@dept) AND lower(full_name) = lower(@fn)", conn);
        cmd.Parameters.AddWithValue("@dept", department);
        cmd.Parameters.AddWithValue("@fn", fullName);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Тот же конфликт, но исключаем самого пользователя (перед PUT /api/admin/users/{id}).
    /// </summary>
    public async Task<bool> ExistsByFullNameAndDeptExceptAsync(long id, string department, string fullName)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT 1 FROM users
            WHERE id != @id AND lower(department) = lower(@dept) AND lower(full_name) = lower(@fn)", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@dept", department);
        cmd.Parameters.AddWithValue("@fn", fullName);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Создание пользователя (POST /api/admin/users). Пароль хешируется pgcrypto, cost=6.
    /// </summary>
    public async Task<long> CreateUserAsync(UserDto dto)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO users (full_name, department, password_hash, is_active)
            VALUES (@fn, @dept, crypt(@pwd, gen_salt('bf', 6)), @active)
            RETURNING id", conn);
        cmd.Parameters.AddWithValue("@fn", dto.fullName.Trim());
        cmd.Parameters.AddWithValue("@dept", dto.department.Trim());
        cmd.Parameters.AddWithValue("@pwd", dto.password);
        cmd.Parameters.AddWithValue("@active", dto.isActive);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Обновление пользователя (PUT /api/admin/users/{id}).
    /// Если password пустой — пароль НЕ трогаем; иначе хешируем заново. Две ветки как в оригинале.
    /// </summary>
    public async Task<bool> UpdateUserAsync(long id, UserDto dto)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();

        NpgsqlCommand cmd = string.IsNullOrWhiteSpace(dto.password)
            ? new NpgsqlCommand(@"
                UPDATE users SET full_name = @fn, department = @dept, is_active = @active
                WHERE id = @id", conn)
            : new NpgsqlCommand(@"
                UPDATE users SET full_name = @fn, department = @dept, is_active = @active,
                    password_hash = crypt(@pwd, gen_salt('bf', 6))
                WHERE id = @id", conn);

        await using (cmd)
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@fn", dto.fullName.Trim());
            cmd.Parameters.AddWithValue("@dept", dto.department.Trim());
            cmd.Parameters.AddWithValue("@active", dto.isActive);
            if (!string.IsNullOrWhiteSpace(dto.password))
                cmd.Parameters.AddWithValue("@pwd", dto.password);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
    }

    /// <summary>
    /// Удаление пользователя (DELETE /api/admin/users/{id}). Сессии удаляются каскадно в БД.
    /// </summary>
    public async Task<bool> DeleteUserAsync(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Переключить активность (PATCH /api/admin/users/{id}/toggle).
    /// Возвращает НОВЫЙ is_active, либо null, если пользователь не найден (эндпоинт отдаст 404).
    /// </summary>
    public async Task<bool?> ToggleUserActiveAsync(long id)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE users SET is_active = NOT is_active WHERE id = @id RETURNING is_active", conn);
        cmd.Parameters.AddWithValue("@id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result != null ? (bool)result : null;
    }
}
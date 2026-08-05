using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// настройки подключения к бд
// Пароль читается из appsettings.Development.json (у каждого компьютера свой).
// Запасной вариант на случай отсутствия файла:
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    connectionString =
        "Host=localhost;" +
        "Port=5432;" +
        "Database=ar_inventory;" +
        "Username=postgres;" +
        "Password=123456Qw;" +
        "SSL Mode=Disable;";
}

builder.Services.AddEndpointsApiExplorer();

// Настройка Swagger (базовая, без зависимостей от Microsoft.OpenApi.Models)
builder.Services.AddSwaggerGen(options =>
{
    // Подключаем XML-комментарии к моделям (для описания полей в схемах)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// порт который будет слушать приложение
builder.WebHost.UseUrls("http://0.0.0.0:8000");

var app = builder.Build();

app.UseCors("AllowAll");

// Раздача статики (admin.html, style.css, main.js) из корня проекта
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        app.Environment.ContentRootPath)
});

// Короткий адрес для админки
app.MapGet("/admin", () => Results.Redirect("/admin.html"));

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AR Inventory API v1");
    options.RoutePrefix = "swagger";

    // Заголовок страницы
    options.DocumentTitle = "AR Inventory API -- Документация";

    // Раскрывать эндпоинты по умолчанию (list = только список, none = всё свёрнуто)
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);

    // Глубина раскрытия моделей
    options.DefaultModelsExpandDepth(2);
    options.DefaultModelExpandDepth(2);

    // Показывать длительность запроса
    options.DisplayRequestDuration();

    // Включить "Try it out" по умолчанию
    options.EnableTryItOutByDefault();

    // Кастомный заголовок в шапке страницы (темная тема topbar)
    options.HeadContent = @"
        <style>
            .swagger-ui .topbar { background-color: #1a1d23; }
            .swagger-ui .topbar .download-url-wrapper .select-label span { color: #ffffff; }
            .swagger-ui .info .title { color: #2563eb; }
        </style>
    ";
});

app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "ok"
    });
})
.WithTags("Public")
.WithSummary("Проверка работоспособности API")
.WithDescription("Возвращает статус ok, если сервер отвечает.")
.Produces(200);

// список отделов из таблицы users
app.MapGet("/api/departments", () =>
{
    var departments = new List<object>();

    using (var conn = new NpgsqlConnection(connectionString))
    {
        conn.Open();

        using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT department
            FROM users
            WHERE is_active = TRUE
            ORDER BY department
        ", conn);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            departments.Add(new
            {
                name = reader.GetString(0)
            });
        }
    }

    return Results.Ok(new
    {
        departments
    });
})
.WithTags("Public")
.WithSummary("Список активных отделов")
.WithDescription("Возвращает уникальные названия отделов пользователей со статусом is_active = true. Используется на экране логина в Unity.")
.Produces(200);

// Вход пользователя
app.MapPost("/api/auth/login", (LoginRequest request) =>
{
    if (request == null ||
        string.IsNullOrWhiteSpace(request.fullName) ||
        string.IsNullOrWhiteSpace(request.department) ||
        string.IsNullOrWhiteSpace(request.password))
    {
        return Results.Json(new
        {
            success = false,
            message = "Заполните ФИО, отдел и пароль"
        }, statusCode: 400);
    }

    var fullName = request.fullName.Trim();
    var department = request.department.Trim();

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var selectCmd = new NpgsqlCommand(@"
    SELECT
        id,
        full_name,
        department
    FROM users
    WHERE is_active = TRUE
      AND lower(department) = lower(@department)
      AND regexp_replace(lower(full_name), '[^a-zа-яё]', '', 'g')
        = regexp_replace(lower(@fullName), '[^a-zа-яё]', '', 'g')
      AND password_hash = crypt(@password, password_hash)
", conn);

    selectCmd.Parameters.AddWithValue("@department", department);
    selectCmd.Parameters.AddWithValue("@fullName", fullName);
    selectCmd.Parameters.AddWithValue("@password", request.password);

    using var reader = selectCmd.ExecuteReader();

    if (!reader.Read())
    {
        return Results.Json(new
        {
            success = false,
            message = "Неверный отдел, ФИО или пароль"
        }, statusCode: 401);
    }

    var userId = reader.GetInt64(0);
    var dbFullName = reader.GetString(1);
    var dbDepartment = reader.GetString(2);

    reader.Close();

    using var insertCmd = new NpgsqlCommand(@"
        INSERT INTO inventory_sessions (
            user_id,
            action,
            comment
        )
        VALUES (@userId, 'login', 'Вход в систему')
        RETURNING id
    ", conn);

    insertCmd.Parameters.AddWithValue("@userId", userId);

    var loginActionId = (long)insertCmd.ExecuteScalar()!;

    return Results.Ok(new
    {
        success = true,
        message = "Вход выполнен",
        userId,
        fullName = dbFullName,
        department = dbDepartment,
        loginActionId
    });
})
.WithTags("Public")
.WithSummary("Вход пользователя")
.WithDescription("Проверяет ФИО, отдел и пароль. ФИО и отдел сравниваются без учёта регистра и знаков препинания. Пароль проверяется через pgcrypto. Возможные ответы: 200 -- успех, 400 -- не заполнены поля, 401 -- неверные учётные данные.")
.Produces(200)
.Produces(400)
.Produces(401);

// Сканирование QR
app.MapPost("/api/scans", (ScanRequest request) =>
{
    if (request == null || string.IsNullOrWhiteSpace(request.qrCode))
    {
        return Results.Json(new
        {
            success = false,
            message = "QR-код не может быть пустым"
        }, statusCode: 400);
    }

    var qrCode = request.qrCode.Trim();

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    // Проверяем пользователя
    using var userCmd = new NpgsqlCommand(@"
        SELECT id
        FROM users
        WHERE id = @userId
          AND is_active = TRUE
    ", conn);

    userCmd.Parameters.AddWithValue("@userId", request.userId);

    var userExists = userCmd.ExecuteScalar() != null;

    if (!userExists)
    {
        return Results.Json(new
        {
            success = false,
            message = "Пользователь не найден"
        }, statusCode: 404);
    }

    // Ищем QR
    using var qrCmd = new NpgsqlCommand(@"
        SELECT
            id,
            object_name
        FROM qr_codes
        WHERE code = @code
          AND is_active = TRUE
    ", conn);

    qrCmd.Parameters.AddWithValue("@code", qrCode);

    long qrId;
    string objectName;

    using (var qrReader = qrCmd.ExecuteReader())
    {
        if (!qrReader.Read())
        {
            return Results.Json(new
            {
                success = false,
                message = $"QR-код {qrCode} не найден"
            }, statusCode: 404);
        }

        qrId = qrReader.GetInt64(0);
        objectName = qrReader.GetString(1);
    }

    // Проверяем, сканировал ли уже этот пользователь этот QR
    using var duplicateCmd = new NpgsqlCommand(@"
        SELECT id
        FROM inventory_sessions
        WHERE user_id = @userId
          AND qr_code_id = @qrCodeId
          AND action = 'scan'
    ", conn);

    duplicateCmd.Parameters.AddWithValue("@userId", request.userId);
    duplicateCmd.Parameters.AddWithValue("@qrCodeId", qrId);

    var alreadyScanned = duplicateCmd.ExecuteScalar() != null;

    var comment = alreadyScanned
        ? "Повторное сканирование QR"
        : "Сканирование QR";

    var result = alreadyScanned
        ? "duplicate"
        : "ok";

    using var insertCmd = new NpgsqlCommand(@"
        INSERT INTO inventory_sessions (
            user_id,
            qr_code_id,
            action,
            comment
        )
        VALUES (@userId, @qrCodeId, 'scan', @comment)
        RETURNING id
    ", conn);

    insertCmd.Parameters.AddWithValue("@userId", request.userId);
    insertCmd.Parameters.AddWithValue("@qrCodeId", qrId);
    insertCmd.Parameters.AddWithValue("@comment", comment);

    var scanId = (long)insertCmd.ExecuteScalar()!;

    return Results.Ok(new
    {
        success = true,
        message = "QR обработан",
        scanId,
        objectName,
        result
    });
})
.WithTags("Public")
.WithSummary("Регистрация сканирования QR-кода")
.WithDescription("Проверяет существование пользователя и QR-кода, определяет повторное сканирование и записывает событие в журнал. Возможные ответы: 200 -- сканирование зарегистрировано, 400 -- пустой QR-код, 404 -- пользователь или QR-код не найдены.")
.Produces(200)
.Produces(400)
.Produces(404);

// АДМИНКА: список всех отделов для выпадающего списка
app.MapGet("/api/admin/departments", () =>
{
    var departments = new List<string>();

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand(@"
        SELECT DISTINCT department
        FROM users
        ORDER BY department
    ", conn);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        departments.Add(reader.GetString(0));
    }

    return Results.Ok(departments);
})
.WithTags("Admin: Users")
.WithSummary("Все отделы (для выпадающего списка)")
.WithDescription("Возвращает список всех отделов без фильтра по активности. Используется в админ-панели.")
.Produces(200);

// АДМИНКА: CRUD для пользователей
app.MapGet("/api/admin/users", () =>
{
    var users = new List<object>();

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand(@"
        SELECT id, full_name, department, is_active, created_at
        FROM users
        ORDER BY department, full_name
    ", conn);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        users.Add(new
        {
            id = Convert.ToInt64(reader.GetValue(0)),
            fullName = reader.GetString(1),
            department = reader.GetString(2),
            isActive = reader.GetBoolean(3),
            createdAt = reader.GetDateTime(4)
        });
    }

    return Results.Ok(users);
})
.WithTags("Admin: Users")
.WithSummary("Список всех пользователей")
.WithDescription("Возвращает всех пользователей, отсортированных по отделу и ФИО. Включая неактивных.")
.Produces(200);

app.MapPost("/api/admin/users", (UserDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.fullName) ||
        string.IsNullOrWhiteSpace(dto.department))
    {
        return Results.Json(new { success = false, message = "ФИО и отдел обязательны" }, statusCode: 400);
    }

    // Если пароль пустой — генерируем случайный (чтобы не сломать UNIQUE constraint)
    // Но обычно нужно требовать пароль при создании
    if (string.IsNullOrWhiteSpace(dto.password))
    {
        return Results.Json(new { success = false, message = "Пароль обязателен при создании" }, statusCode: 400);
    }

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    // Проверяем уникальность ФИО+отдел
    using var checkCmd = new NpgsqlCommand(@"
        SELECT 1 FROM users 
        WHERE lower(department) = lower(@dept) 
          AND lower(full_name) = lower(@fn)
    ", conn);
    checkCmd.Parameters.AddWithValue("@dept", dto.department.Trim());
    checkCmd.Parameters.AddWithValue("@fn", dto.fullName.Trim());

    if (checkCmd.ExecuteScalar() != null)
    {
        return Results.Json(new { success = false, message = "Пользователь с таким ФИО и отделом уже существует" }, statusCode: 409);
    }

    // Хешируем пароль через pgcrypto (cost=6 как в твоем дампе)
    using var insertCmd = new NpgsqlCommand(@"
        INSERT INTO users (full_name, department, password_hash, is_active)
        VALUES (@fn, @dept, crypt(@pwd, gen_salt('bf', 6)), @active)
        RETURNING id
    ", conn);
    insertCmd.Parameters.AddWithValue("@fn", dto.fullName.Trim());
    insertCmd.Parameters.AddWithValue("@dept", dto.department.Trim());
    insertCmd.Parameters.AddWithValue("@pwd", dto.password);
    insertCmd.Parameters.AddWithValue("@active", dto.isActive);

    var newId = Convert.ToInt64(insertCmd.ExecuteScalar());

    return Results.Ok(new { success = true, id = newId, message = "Пользователь создан" });
})
.WithTags("Admin: Users")
.WithSummary("Создание нового пользователя")
.WithDescription("Пароль хешируется через pgcrypto (bcrypt, cost=6). Сочетание ФИО+отдел должно быть уникальным. Возможные ответы: 200 -- создан, 400 -- не заполнены поля, 409 -- конфликт уникальности.")
.Produces(200)
.Produces(400)
.Produces(409);

app.MapPut("/api/admin/users/{id:long}", (long id, UserDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.fullName) ||
        string.IsNullOrWhiteSpace(dto.department))
    {
        return Results.Json(new { success = false, message = "ФИО и отдел обязательны" }, statusCode: 400);
    }

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    // Проверяем уникальность (исключая текущего пользователя)
    using var checkCmd = new NpgsqlCommand(@"
        SELECT 1 FROM users 
        WHERE id != @id
          AND lower(department) = lower(@dept) 
          AND lower(full_name) = lower(@fn)
    ", conn);
    checkCmd.Parameters.AddWithValue("@id", id);
    checkCmd.Parameters.AddWithValue("@dept", dto.department.Trim());
    checkCmd.Parameters.AddWithValue("@fn", dto.fullName.Trim());

    if (checkCmd.ExecuteScalar() != null)
    {
        return Results.Json(new { success = false, message = "Пользователь с таким ФИО и отделом уже существует" }, statusCode: 409);
    }

    NpgsqlCommand updateCmd;
    if (string.IsNullOrWhiteSpace(dto.password))
    {
        // Обновляем без смены пароля
        updateCmd = new NpgsqlCommand(@"
            UPDATE users 
            SET full_name = @fn, department = @dept, is_active = @active
            WHERE id = @id
        ", conn);
    }
    else
    {
        // Меняем пароль
        updateCmd = new NpgsqlCommand(@"
            UPDATE users 
            SET full_name = @fn, department = @dept, is_active = @active,
                password_hash = crypt(@pwd, gen_salt('bf', 6))
            WHERE id = @id
        ", conn);
        updateCmd.Parameters.AddWithValue("@pwd", dto.password);
    }

    updateCmd.Parameters.AddWithValue("@id", id);
    updateCmd.Parameters.AddWithValue("@fn", dto.fullName.Trim());
    updateCmd.Parameters.AddWithValue("@dept", dto.department.Trim());
    updateCmd.Parameters.AddWithValue("@active", dto.isActive);

    var affected = updateCmd.ExecuteNonQuery();
    return affected > 0
        ? Results.Ok(new { success = true, message = "Пользователь обновлен" })
        : Results.NotFound(new { success = false, message = "Пользователь не найден" });
})
.WithTags("Admin: Users")
.WithSummary("Обновление пользователя")
.WithDescription("Если поле password пустое, пароль остаётся прежним. Иначе хешируется заново. Возможные ответы: 200 -- обновлён, 400 -- не заполнены поля, 404 -- не найден, 409 -- конфликт уникальности.")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409);

app.MapDelete("/api/admin/users/{id:long}", (long id) =>
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
    cmd.Parameters.AddWithValue("@id", id);

    var affected = cmd.ExecuteNonQuery();
    return affected > 0
        ? Results.Ok(new { success = true, message = "Пользователь удален" })
        : Results.NotFound(new { success = false, message = "Пользователь не найден" });
})
.WithTags("Admin: Users")
.WithSummary("Удаление пользователя")
.WithDescription("Удаляет пользователя. Его сессии в inventory_sessions удаляются каскадно (ON DELETE CASCADE). Возможные ответы: 200 -- удалён, 404 -- не найден.")
.Produces(200)
.Produces(404);

app.MapPatch("/api/admin/users/{id:long}/toggle", (long id) =>
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand(@"
        UPDATE users SET is_active = NOT is_active 
        WHERE id = @id
        RETURNING is_active
    ", conn);
    cmd.Parameters.AddWithValue("@id", id);

    var result = cmd.ExecuteScalar();
    return result != null
        ? Results.Ok(new { success = true, isActive = (bool)result })
        : Results.NotFound(new { success = false, message = "Пользователь не найден" });
})
.WithTags("Admin: Users")
.WithSummary("Переключить статус активности")
.WithDescription("Инвертирует флаг is_active. Неактивные пользователи не могут входить в систему. Возможные ответы: 200 -- статус изменён, 404 -- не найден.")
.Produces(200)
.Produces(404);

// АДМИНКА: CRUD для QR-кодов

app.MapGet("/api/admin/qr", () =>
{
    var items = new List<object>();

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand(@"
        SELECT id, code, object_name, is_active, created_at
        FROM qr_codes
        ORDER BY object_name
    ", conn);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
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

    return Results.Ok(items);
})
.WithTags("Admin: QR")
.WithSummary("Список всех QR-кодов")
.Produces(200);

app.MapPost("/api/admin/qr", (QrDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.code) || string.IsNullOrWhiteSpace(dto.objectName))
    {
        return Results.Json(new { success = false, message = "Код и имя объекта обязательны" }, statusCode: 400);
    }

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    // Проверка уникальности кода
    using var checkCmd = new NpgsqlCommand("SELECT 1 FROM qr_codes WHERE code = @code", conn);
    checkCmd.Parameters.AddWithValue("@code", dto.code.Trim());

    if (checkCmd.ExecuteScalar() != null)
    {
        return Results.Json(new { success = false, message = "QR-код с таким значением уже существует" }, statusCode: 409);
    }

    using var insertCmd = new NpgsqlCommand(@"
        INSERT INTO qr_codes (code, object_name, is_active)
        VALUES (@code, @obj, @active)
        RETURNING id
    ", conn);
    insertCmd.Parameters.AddWithValue("@code", dto.code.Trim());
    insertCmd.Parameters.AddWithValue("@obj", dto.objectName.Trim());
    insertCmd.Parameters.AddWithValue("@active", dto.isActive);

    var newId = (long)insertCmd.ExecuteScalar()!;
    return Results.Ok(new { success = true, id = newId, message = "QR-код создан" });
})
.WithTags("Admin: QR")
.WithSummary("Создание QR-кода")
.WithDescription("Значение поля code должно быть уникальным во всей таблице. Возможные ответы: 200 -- создан, 400 -- не заполнены поля, 409 -- конфликт уникальности.")
.Produces(200)
.Produces(400)
.Produces(409);

app.MapPut("/api/admin/qr/{id:long}", (long id, QrDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.code) || string.IsNullOrWhiteSpace(dto.objectName))
    {
        return Results.Json(new { success = false, message = "Код и имя объекта обязательны" }, statusCode: 400);
    }

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var checkCmd = new NpgsqlCommand(@"
        SELECT 1 FROM qr_codes WHERE id != @id AND code = @code
    ", conn);
    checkCmd.Parameters.AddWithValue("@id", id);
    checkCmd.Parameters.AddWithValue("@code", dto.code.Trim());

    if (checkCmd.ExecuteScalar() != null)
    {
        return Results.Json(new { success = false, message = "QR-код с таким значением уже существует" }, statusCode: 409);
    }

    using var updateCmd = new NpgsqlCommand(@"
        UPDATE qr_codes 
        SET code = @code, object_name = @obj, is_active = @active
        WHERE id = @id
    ", conn);
    updateCmd.Parameters.AddWithValue("@id", id);
    updateCmd.Parameters.AddWithValue("@code", dto.code.Trim());
    updateCmd.Parameters.AddWithValue("@obj", dto.objectName.Trim());
    updateCmd.Parameters.AddWithValue("@active", dto.isActive);

    var affected = updateCmd.ExecuteNonQuery();
    return affected > 0
        ? Results.Ok(new { success = true, message = "QR-код обновлен" })
        : Results.NotFound(new { success = false, message = "QR-код не найден" });
})
.WithTags("Admin: QR")
.WithSummary("Обновление QR-кода")
.WithDescription("Возможные ответы: 200 -- обновлён, 400 -- не заполнены поля, 404 -- не найден, 409 -- конфликт уникальности кода.")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409);

app.MapDelete("/api/admin/qr/{id:long}", (long id) =>
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand("DELETE FROM qr_codes WHERE id = @id", conn);
    cmd.Parameters.AddWithValue("@id", id);

    var affected = cmd.ExecuteNonQuery();
    return affected > 0
        ? Results.Ok(new { success = true, message = "QR-код удален" })
        : Results.NotFound(new { success = false, message = "QR-код не найден" });
})
.WithTags("Admin: QR")
.WithSummary("Удаление QR-кода")
.WithDescription("Связанные сессии в inventory_sessions не удаляются, но поле qr_code_id становится NULL (ON DELETE SET NULL). Возможные ответы: 200 -- удалён, 404 -- не найден.")
.Produces(200)
.Produces(404);

// АДМИНКА: журнал сессий

app.MapGet("/api/admin/sessions", () =>
{
    var sessions = new List<object>();

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand(@"
        SELECT 
            s.id,
            s.action,
            s.action_time,
            s.comment,
            u.full_name,
            u.department,
            q.code,
            q.object_name
        FROM inventory_sessions s
        JOIN users u ON u.id = s.user_id
        LEFT JOIN qr_codes q ON q.id = s.qr_code_id
        ORDER BY s.action_time DESC
        LIMIT 500
    ", conn);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
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

    return Results.Ok(sessions);
})
.WithTags("Admin: Sessions")
.WithSummary("Журнал событий (последние 500)")
.WithDescription("Логины и сканирования с информацией о пользователе и (для сканирований) о QR-коде. Сортировка по убыванию времени.")
.Produces(200);

// АДМИНКА: удаление одной записи журнала
app.MapDelete("/api/admin/sessions/{id:long}", (long id) =>
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand("DELETE FROM inventory_sessions WHERE id = @id", conn);
    cmd.Parameters.AddWithValue("@id", id);

    var affected = cmd.ExecuteNonQuery();
    return affected > 0
        ? Results.Ok(new { success = true, message = "Запись удалена" })
        : Results.NotFound(new { success = false, message = "Запись не найдена" });
})
.WithTags("Admin: Sessions")
.WithSummary("Удаление одной записи журнала")
.WithDescription("Удаляет запись по id. Возможные ответы: 200 -- удалена, 404 -- не найдена.")
.Produces(200)
.Produces(404);

// АДМИНКА: очистка всего журнала
app.MapDelete("/api/admin/sessions", () =>
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();

    using var cmd = new NpgsqlCommand("DELETE FROM inventory_sessions", conn);
    var affected = cmd.ExecuteNonQuery();

    return Results.Ok(new { success = true, message = $"Удалено записей: {affected}" });
})
.WithTags("Admin: Sessions")
.WithSummary("Очистить весь журнал")
.WithDescription("Удаляет все записи журнала. Используйте с осторожностью.")
.Produces(200);

// синхронизация приём записей с телефона
app.MapPost("/api/sync/sessions", (SyncPayloadDto payload) =>
{
    if (payload?.items == null || payload.items.Count == 0)
        return Results.Ok(new { imported = 0, skipped = 0 });

    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();
    int imported = 0, skipped = 0;

    foreach (var s in payload.items)
    {
        long userId = 0;

        // 1) Ищем по ФИО + отдел (надёжно, не зависит от ids)
        if (!string.IsNullOrWhiteSpace(s.fullName) && !string.IsNullOrWhiteSpace(s.department))
        {
            using var ucmd = new NpgsqlCommand(@"
                SELECT id FROM users
                WHERE lower(full_name) = lower(@fn) AND lower(department) = lower(@dep)
            ", conn);
            ucmd.Parameters.AddWithValue("@fn", s.fullName.Trim());
            ucmd.Parameters.AddWithValue("@dep", s.department.Trim());
            var r = ucmd.ExecuteScalar();
            if (r != null) userId = Convert.ToInt64(r);
        }

        // 2) Запасной вариант — по id
        if (userId == 0)
        {
            using var ucmd2 = new NpgsqlCommand("SELECT id FROM users WHERE id = @id", conn);
            ucmd2.Parameters.AddWithValue("@id", s.userId);
            var r2 = ucmd2.ExecuteScalar();
            if (r2 != null) userId = Convert.ToInt64(r2);
        }

        if (userId == 0) { skipped++; continue; }

        // QR: если есть в базе — привязываем, иначе NULL
        object qrParam = DBNull.Value;
        if (s.qrCodeId > 0)
        {
            using var qcmd = new NpgsqlCommand("SELECT id FROM qr_codes WHERE id = @id", conn);
            qcmd.Parameters.AddWithValue("@id", s.qrCodeId);
            if (qcmd.ExecuteScalar() != null) qrParam = s.qrCodeId;
        }

        // Разбираем время из ISO-строки; если не вышло — берём текущее UTC
        DateTime time = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(s.actionTime) &&
            DateTime.TryParse(s.actionTime,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            time = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment, action_time)
            VALUES (@userId, @qrId, @action, @comment, @time)
        ", conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@qrId", qrParam);
        cmd.Parameters.AddWithValue("@action", s.action);
        cmd.Parameters.AddWithValue("@comment", string.IsNullOrEmpty(s.comment) ? (object)DBNull.Value : s.comment);
        // ИСПРАВЛЕНО: используем уже распарсенную переменную time (UTC), а не s.actionTime (строка)
        cmd.Parameters.AddWithValue("@time", time);
        cmd.ExecuteNonQuery();
        imported++;
    }

    return Results.Ok(new { imported, skipped });
});

app.Run();

/// <summary>
/// Запрос на вход в систему.
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// Полное имя сотрудника (например, "Иванов И.И.").
    /// </summary>
    public string fullName { get; set; } = string.Empty;

    /// <summary>
    /// Название отдела (должно совпадать с записью в БД без учёта регистра и пунктуации).
    /// </summary>
    public string department { get; set; } = string.Empty;

    /// <summary>
    /// Пароль пользователя в открытом виде.
    /// </summary>
    public string password { get; set; } = string.Empty;
}

/// <summary>
/// Запрос на регистрацию сканирования QR-кода.
/// </summary>
public class ScanRequest
{
    /// <summary>
    /// ID авторизованного пользователя, полученный при входе.
    /// </summary>
    public long userId { get; set; }

    /// <summary>
    /// Значение QR-кода, считанное камерой устройства.
    /// </summary>
    public string qrCode { get; set; } = string.Empty;
}

/// <summary>
/// Данные для создания или обновления пользователя.
/// </summary>
public class UserDto
{
    /// <summary>
    /// Полное имя сотрудника (например, "Иванов И.И.").
    /// </summary>
    public string fullName { get; set; } = string.Empty;

    /// <summary>
    /// Название отдела. Должно совпадать с существующим отделом (см. /api/admin/departments) или быть новым.
    /// </summary>
    public string department { get; set; } = string.Empty;

    /// <summary>
    /// Пароль в открытом виде. Будет захеширован на сервере через pgcrypto.
    /// При обновлении оставьте пустым, чтобы сохранить текущий пароль.
    /// </summary>
    public string password { get; set; } = string.Empty;

    /// <summary>
    /// Флаг активности. Неактивные пользователи не могут авторизоваться в приложении.
    /// </summary>
    public bool isActive { get; set; } = true;
}

/// <summary>
/// Данные для создания или обновления QR-кода.
/// </summary>
public class QrDto
{
    /// <summary>
    /// Уникальное значение QR-кода. Может быть URL, идентификатором или произвольной строкой.
    /// </summary>
    public string code { get; set; } = string.Empty;

    /// <summary>
    /// Человеко-читаемое название объекта, к которому привязан QR-код.
    /// </summary>
    public string objectName { get; set; } = string.Empty;

    /// <summary>
    /// Флаг активности. Неактивные коды игнорируются приложением при сканировании.
    /// </summary>
    public bool isActive { get; set; } = true;
}

public class SyncPayloadDto
{
    public List<SyncSessionDto> items { get; set; } = new();
}

public class SyncSessionDto
{
    public long userId { get; set; }
    public long qrCodeId { get; set; }
    public string action { get; set; } = string.Empty;
    public string comment { get; set; } = string.Empty;
    public string actionTime { get; set; } = string.Empty;
    public string fullName { get; set; } = string.Empty;
    public string department { get; set; } = string.Empty;
}
using Backend.Contracts;
using Backend.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Публичные эндпоинты, доступные без авторизации (используются Unity-клиентом):
/// - GET  /api/departments  — список активных отделов для экрана логина
/// - POST /api/auth/login   — вход пользователя
/// - POST /api/scans        — регистрация сканирования QR-кода
/// 
/// Все ответы JSON сохранены чтобы мобильное приложение не потребовало изменений.
/// </summary>
public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Список активных отделов для экрана логина в Unity.
        // Обратите внимание: формат ответа { departments: [...] } — объект с полем,
        // в отличие от админского эндпоинта, который возвращает просто массив.
        endpoints.MapGet("/api/departments", async (UserRepository users) =>
        {
            var departments = await users.GetActiveDepartmentsAsync();
            return Results.Ok(new { departments });
        })
        .WithTags("Public")
        .WithSummary("Список активных отделов")
        .WithDescription("Возвращает уникальные названия отделов пользователей со статусом is_active = true. Используется на экране логина в Unity.")
        .Produces(200);

        // Вход пользователя. ФИО и отдел сравниваются без учёта регистра и знаков препинания,
        // пароль проверяется через pgcrypto. При успехе создаётся запись в журнале сессий.
        endpoints.MapPost("/api/auth/login", async (LoginRequest request, UserRepository users, SessionRepository sessions) =>
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.fullName) ||
                string.IsNullOrWhiteSpace(request.department) ||
                string.IsNullOrWhiteSpace(request.password))
            {
                return Results.Json(new { success = false, message = "Заполните ФИО, отдел и пароль" }, statusCode: 400);
            }

            var user = await users.LoginAsync(request.fullName.Trim(), request.department.Trim(), request.password);
            if (user is null)
            {
                return Results.Json(new { success = false, message = "Неверный отдел, ФИО или пароль" }, statusCode: 401);
            }

            var (id, fullName, department) = user.Value;
            var loginActionId = await sessions.LogSessionAsync(id, "login", "Вход в систему");

            return Results.Ok(new
            {
                success = true,
                message = "Вход выполнен",
                userId = id,
                fullName,
                department,
                loginActionId
            });
        })
        .WithTags("Public")
        .WithSummary("Вход пользователя")
        .WithDescription("Проверяет ФИО, отдел и пароль. ФИО и отдел сравниваются без учёта регистра и знаков препинания. Пароль проверяется через pgcrypto. Возможные ответы: 200 — успех, 400 — не заполнены поля, 401 — неверные учётные данные.")
        .Produces(200)
        .Produces(400)
        .Produces(401);

        // Регистрация сканирования QR-кода. Проверяет существование пользователя и QR,
        // определяет повторное сканирование и записывает событие в журнал.
        endpoints.MapPost("/api/scans", async (ScanRequest request, UserRepository users, QrRepository qrRepo, SessionRepository sessions) =>
        {
            if (request == null || string.IsNullOrWhiteSpace(request.qrCode))
            {
                return Results.Json(new { success = false, message = "QR-код не может быть пустым" }, statusCode: 400);
            }

            var qrCode = request.qrCode.Trim();

            // 1. Проверяем пользователя
            if (!await users.UserExistsAsync(request.userId))
            {
                return Results.Json(new { success = false, message = "Пользователь не найден" }, statusCode: 404);
            }

            // 2. Ищем QR
            var qr = await qrRepo.GetQrByCodeAsync(qrCode);
            if (qr is null)
            {
                return Results.Json(new { success = false, message = $"QR-код {qrCode} не найден" }, statusCode: 404);
            }

            var (qrId, objectName) = qr.Value;

            // 3. Проверяем, сканировал ли уже этот пользователь этот QR
            var alreadyScanned = await sessions.HasScanSessionAsync(request.userId, qrId);
            var comment = alreadyScanned ? "Повторное сканирование QR" : "Сканирование QR";
            var result = alreadyScanned ? "duplicate" : "ok";

            // 4. Записываем событие в журнал
            var scanId = await sessions.LogSessionAsync(request.userId, "scan", comment, qrId);

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
        .WithDescription("Проверяет существование пользователя и QR-кода, определяет повторное сканирование и записывает событие в журнал. Возможные ответы: 200 — сканирование зарегистрировано, 400 — пустой QR-код, 404 — пользователь или QR-код не найдены.")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        return endpoints;
    }
}
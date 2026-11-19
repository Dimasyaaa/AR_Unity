using Backend.Contracts;
using Backend.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Админские эндпоинты для управления пользователями:
/// - GET    /api/admin/departments        — все отделы (для выпадающего списка)
/// - GET    /api/admin/users              — список всех пользователей (включая неактивных)
/// - POST   /api/admin/users              — создание пользователя
/// - PUT    /api/admin/users/{id}         — обновление пользователя
/// - DELETE /api/admin/users/{id}         — удаление пользователя
/// - PATCH  /api/admin/users/{id}/toggle  — переключение активности
/// 
/// Все эндпоинты требуют авторизации администратора (в текущей версии — без проверки токена,
/// так как в оригинале авторизация не была реализована).
/// </summary>
public static class UserAdminEndpoints
{
    public static IEndpointRouteBuilder MapUserAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Все отделы для выпадающего списка в админке (без фильтра по активности)
        endpoints.MapGet("/api/admin/departments", async (UserRepository users) =>
        {
            var departments = await users.GetAllDepartmentsAsync();
            return Results.Ok(departments);
        })
        .WithTags("Admin: Users")
        .WithSummary("Все отделы (для выпадающего списка)")
        .WithDescription("Возвращает список всех отделов без фильтра по активности. Используется в админ-панели.")
        .Produces(200);

        // Список всех пользователей
        endpoints.MapGet("/api/admin/users", async (UserRepository users) =>
        {
            var usersList = await users.GetAllUsersAsync();
            return Results.Ok(usersList);
        })
        .WithTags("Admin: Users")
        .WithSummary("Список всех пользователей")
        .WithDescription("Возвращает всех пользователей, отсортированных по отделу и ФИО. Включая неактивных.")
        .Produces(200);

        // Создание пользователя
        endpoints.MapPost("/api/admin/users", async (UserDto dto, UserRepository users) =>
        {
            if (string.IsNullOrWhiteSpace(dto.fullName) || string.IsNullOrWhiteSpace(dto.department))
            {
                return Results.Json(new { success = false, message = "ФИО и отдел обязательны" }, statusCode: 400);
            }

            if (string.IsNullOrWhiteSpace(dto.password))
            {
                return Results.Json(new { success = false, message = "Пароль обязателен при создании" }, statusCode: 400);
            }

            // Проверяем уникальность ФИО+отдел
            if (await users.ExistsByFullNameAndDeptAsync(dto.department.Trim(), dto.fullName.Trim()))
            {
                return Results.Json(new { success = false, message = "Пользователь с таким ФИО и отделом уже существует" }, statusCode: 409);
            }

            var newId = await users.CreateUserAsync(dto);
            return Results.Ok(new { success = true, id = newId, message = "Пользователь создан" });
        })
        .WithTags("Admin: Users")
        .WithSummary("Создание нового пользователя")
        .WithDescription("Пароль хешируется через pgcrypto (bcrypt, cost=6). Сочетание ФИО+отдел должно быть уникальным. Возможные ответы: 200 — создан, 400 — не заполнены поля, 409 — конфликт уникальности.")
        .Produces(200)
        .Produces(400)
        .Produces(409);

        // Обновление пользователя
        endpoints.MapPut("/api/admin/users/{id:long}", async (long id, UserDto dto, UserRepository users) =>
        {
            if (string.IsNullOrWhiteSpace(dto.fullName) || string.IsNullOrWhiteSpace(dto.department))
            {
                return Results.Json(new { success = false, message = "ФИО и отдел обязательны" }, statusCode: 400);
            }

            // Проверяем уникальность (исключая текущего пользователя)
            if (await users.ExistsByFullNameAndDeptExceptAsync(id, dto.department.Trim(), dto.fullName.Trim()))
            {
                return Results.Json(new { success = false, message = "Пользователь с таким ФИО и отделом уже существует" }, statusCode: 409);
            }

            var updated = await users.UpdateUserAsync(id, dto);
            return updated
                ? Results.Ok(new { success = true, message = "Пользователь обновлен" })
                : Results.NotFound(new { success = false, message = "Пользователь не найден" });
        })
        .WithTags("Admin: Users")
        .WithSummary("Обновление пользователя")
        .WithDescription("Если поле password пустое, пароль остаётся прежним. Иначе хешируется заново. Возможные ответы: 200 — обновлён, 400 — не заполнены поля, 404 — не найден, 409 — конфликт уникальности.")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .Produces(409);

        // Удаление пользователя
        endpoints.MapDelete("/api/admin/users/{id:long}", async (long id, UserRepository users) =>
        {
            var deleted = await users.DeleteUserAsync(id);
            return deleted
                ? Results.Ok(new { success = true, message = "Пользователь удален" })
                : Results.NotFound(new { success = false, message = "Пользователь не найден" });
        })
        .WithTags("Admin: Users")
        .WithSummary("Удаление пользователя")
        .WithDescription("Удаляет пользователя. Его сессии в inventory_sessions удаляются каскадно (ON DELETE CASCADE). Возможные ответы: 200 — удалён, 404 — не найден.")
        .Produces(200)
        .Produces(404);

        // Переключить статус активности
        endpoints.MapPatch("/api/admin/users/{id:long}/toggle", async (long id, UserRepository users) =>
        {
            var newIsActive = await users.ToggleUserActiveAsync(id);
            return newIsActive.HasValue
                ? Results.Ok(new { success = true, isActive = newIsActive.Value })
                : Results.NotFound(new { success = false, message = "Пользователь не найден" });
        })
        .WithTags("Admin: Users")
        .WithSummary("Переключить статус активности")
        .WithDescription("Инвертирует флаг is_active. Неактивные пользователи не могут входить в систему. Возможные ответы: 200 — статус изменён, 404 — не найден.")
        .Produces(200)
        .Produces(404);

        return endpoints;
    }
}
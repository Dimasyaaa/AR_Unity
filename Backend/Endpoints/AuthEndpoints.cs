using Backend.Contracts;
using Backend.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (LoginRequest request, UserRepository users, SessionRepository sessions) =>
        {
            if (string.IsNullOrWhiteSpace(request.fullName) || 
                string.IsNullOrWhiteSpace(request.department) || 
                string.IsNullOrWhiteSpace(request.password))
            {
                return Results.Json(new { success = false, message = "Заполните ФИО, отдел и пароль" }, statusCode: 400);
            }

            // 1. Получаем nullable-кортеж
            var userResult = await users.LoginAsync(request.fullName.Trim(), request.department.Trim(), request.password);
            
            // 2. Проверяем на null
            if (userResult == null)
            {
                return Results.Json(new { success = false, message = "Неверный отдел, ФИО или пароль" }, statusCode: 401);
            }

            // 3. Явно распаковываем кортеж (это убирает ошибки CS1061)
            var (id, fullName, department) = userResult.Value;

            // 4. Используем распакованные переменные
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
        .Produces(200)
        .Produces(400)
        .Produces(401);

        return endpoints;
    }
}
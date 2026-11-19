using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Системные маршруты: редиректы на админку и health-check.
/// Эти эндпоинты не требуют БД и не зависят от репозиториев.
/// Редиректы с "/" и "/admin" на "/admin.html" нужны, потому что
/// UseStaticFiles сам по себе не делает редирект с корня сайта.
/// </summary>
public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Короткий адрес для открытия админки: http://localhost:8000/admin
        endpoints.MapGet("/admin", () => Results.Redirect("/admin.html"))
            .ExcludeFromDescription() // не показывать в Swagger
            .WithTags("System");

        // Корень сайта тоже ведёт на админку
        endpoints.MapGet("/", () => Results.Redirect("/admin.html"))
            .ExcludeFromDescription()
            .WithTags("System");

        // Health-check для мониторинга (используется Unity-клиентом и CI)
        endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Public")
            .WithSummary("Проверка работоспособности API")
            .WithDescription("Возвращает статус ok, если сервер отвечает.")
            .Produces(200);

        return endpoints;
    }
}
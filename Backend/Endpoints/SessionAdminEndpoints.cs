using Backend.Contracts;
using Backend.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Админские эндпоинты для журнала сессий и синхронизации с мобильного приложения.
/// </summary>
public static class SessionAdminEndpoints
{
    public static IEndpointRouteBuilder MapSessionAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/sessions", async (SessionRepository sessions) =>
        {
            var list = await sessions.GetSessionsAsync();
            return Results.Ok(list);
        })
        .WithTags("Admin: Sessions")
        .WithSummary("Журнал событий (последние 500)")
        .Produces(200);

        endpoints.MapDelete("/api/admin/sessions/{id:long}", async (long id, SessionRepository sessions) =>
        {
            var deleted = await sessions.DeleteSessionAsync(id);
            return deleted
                ? Results.Ok(new { success = true, message = "Запись удалена" })
                : Results.NotFound(new { success = false, message = "Запись не найдена" });
        })
        .WithTags("Admin: Sessions")
        .WithSummary("Удаление одной записи журнала")
        .Produces(200).Produces(404);

        endpoints.MapDelete("/api/admin/sessions", async (SessionRepository sessions) =>
        {
            var affected = await sessions.DeleteAllSessionsAsync();
            return Results.Ok(new { success = true, message = $"Удалено записей: {affected}" });
        })
        .WithTags("Admin: Sessions")
        .WithSummary("Очистить весь журнал")
        .Produces(200);

        endpoints.MapPost("/api/sync/sessions", async (SyncPayloadDto payload, SessionRepository sessions) =>
        {
            if (payload?.items == null || payload.items.Count == 0)
                return Results.Ok(new { imported = 0, skipped = 0 });

            var result = await sessions.SyncSessionsAsync(payload.items);
            return Results.Ok(new { imported = result.imported, skipped = result.skipped });
        })
        .WithTags("Public") // Оставлен тег Public, так как вызывается с телефона без админской авторизации
        .WithSummary("Синхронизация офлайн-сессий")
        .Produces(200);

        return endpoints;
    }
}
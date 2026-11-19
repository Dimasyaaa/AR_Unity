using Backend.Contracts;
using Backend.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Админские эндпоинты для управления QR-кодами.
/// Удаление использует QrDeletionRepository для транзакционного каскада.
/// </summary>
public static class QrAdminEndpoints
{
    public static IEndpointRouteBuilder MapQrAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/qr", async (QrRepository qrRepo) =>
        {
            var items = await qrRepo.GetAllQrCodesAsync();
            return Results.Ok(items);
        })
        .WithTags("Admin: QR")
        .WithSummary("Список всех QR-кодов")
        .Produces(200);

        endpoints.MapPost("/api/admin/qr", async (QrDto dto, QrRepository qrRepo) =>
        {
            if (string.IsNullOrWhiteSpace(dto.code) || string.IsNullOrWhiteSpace(dto.objectName))
                return Results.Json(new { success = false, message = "Код и имя объекта обязательны" }, statusCode: 400);

            if (await qrRepo.ExistsByCodeAsync(dto.code.Trim()))
                return Results.Json(new { success = false, message = "QR-код с таким значением уже существует" }, statusCode: 409);

            var newId = await qrRepo.CreateQrAsync(dto);
            return Results.Ok(new { success = true, id = newId, message = "QR-код создан" });
        })
        .WithTags("Admin: QR")
        .WithSummary("Создание QR-кода")
        .Produces(200).Produces(400).Produces(409);

        endpoints.MapPut("/api/admin/qr/{id:long}", async (long id, QrDto dto, QrRepository qrRepo) =>
        {
            if (string.IsNullOrWhiteSpace(dto.code) || string.IsNullOrWhiteSpace(dto.objectName))
                return Results.Json(new { success = false, message = "Код и имя объекта обязательны" }, statusCode: 400);

            if (await qrRepo.ExistsByCodeExceptAsync(id, dto.code.Trim()))
                return Results.Json(new { success = false, message = "QR-код с таким значением уже существует" }, statusCode: 409);

            var updated = await qrRepo.UpdateQrAsync(id, dto);
            return updated
                ? Results.Ok(new { success = true, message = "QR-код обновлен" })
                : Results.NotFound(new { success = false, message = "QR-код не найден" });
        })
        .WithTags("Admin: QR")
        .WithSummary("Обновление QR-кода")
        .Produces(200).Produces(400).Produces(404).Produces(409);

        endpoints.MapDelete("/api/admin/qr/{id:long}", async (long id, QrDeletionRepository qrDeletion) =>
        {
            var deletion = await qrDeletion.DeleteQrAndDependenciesAsync(id, requireCabinet: false);
            if (!deletion.QrFound)
                return Results.NotFound(new { success = false, message = "QR-код не найден" });

            return Results.Ok(new
            {
                success = true,
                message = deletion.CabinetFound
                    ? "Шкаф, QR-код и история инвентаризаций удалены"
                    : "QR-код удалён; события журнала сохранены без связи с ним",
                deletion.DeletedRunItems,
                deletion.DeletedRuns,
                deletion.DeletedChecklistItems,
                deletion.DeletedPhotos,
                deletion.DeletedEvents
            });
        })
        .WithTags("Admin: QR")
        .WithSummary("Удаление QR-кода")
        .Produces(200).Produces(404);

        return endpoints;
    }
}

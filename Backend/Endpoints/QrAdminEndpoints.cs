using Backend.Contracts;
using Backend.Data;
using Backend.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QRCoder;

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

        endpoints.MapPost("/api/admin/qr/inventory", async (
            InventoryQrCreateDto request, QrRepository qrRepo) =>
        {
            if (request is null)
                return Results.BadRequest(new { success = false, message = "Укажите ЕНС/СКК и учётное количество." });

            string code;
            try
            {
                code = QrHelper.CreateInventoryPayload(request.ensCode, request.accountingQuantity);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { success = false, message = error.Message });
            }

            if (await qrRepo.ExistsByCodeAsync(code))
                return Results.Conflict(new { success = false, message = "QR с такой парой ЕНС/СКК и количества уже создан." });

            var newId = await qrRepo.CreateQrAsync(new QrDto
            {
                code = code,
                objectName = $"МТР {request.ensCode.Trim()}",
                isActive = true
            });
            return Results.Ok(new { success = true, id = newId, code, message = "QR-код МТР создан." });
        })
        .WithTags("Admin: QR")
        .WithSummary("Создать QR для одной строки МТР")
        .Produces(200).Produces(400).Produces(409);

        endpoints.MapGet("/api/admin/qr/{id:long}/image", async (long id, QrRepository qrRepo) =>
        {
            string? code = await qrRepo.GetCodeByIdAsync(id);
            if (code is null) return Results.NotFound(new { message = "Активный QR-код не найден." });

            return Results.Bytes(CreateQrPng(code), "image/png");
        })
        .WithTags("Admin: QR")
        .WithSummary("Получить PNG-изображение QR-кода для печати")
        .Produces(200).Produces(404);

        endpoints.MapGet("/api/admin/qr/preview", (string? ensCode, int? accountingQuantity) =>
        {
            string code;
            try
            {
                code = QrHelper.CreateInventoryPayload(ensCode ?? string.Empty, accountingQuantity ?? 0);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { message = error.Message });
            }

            return Results.Bytes(CreateQrPng(code), "image/png");
        })
        .WithTags("Admin: QR")
        .WithSummary("Сформировать изображение QR до сохранения этикетки")
        .Produces(200).Produces(400);

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
            var deletion = await qrDeletion.DeleteQrAndDependenciesAsync(id, requireBox: false);
            if (!deletion.QrFound)
                return Results.NotFound(new { success = false, message = "QR-код не найден" });

            return Results.Ok(new
            {
                success = true,
                message = deletion.BoxFound
                    ? "Коробка, QR-код и история инвентаризаций удалены"
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

    /// <summary>Создаёт PNG-картинку с достаточной коррекцией ошибок для печатной этикетки.</summary>
    private static byte[] CreateQrPng(string code)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(12);
    }
}

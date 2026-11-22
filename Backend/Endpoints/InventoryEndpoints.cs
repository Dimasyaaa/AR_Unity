using Backend.Data;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Linq;

namespace Backend.Endpoints;

/// <summary>
/// Публичные эндпоинты инвентаризации для Unity-клиента.
/// </summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Современный адрес описывает содержимое именно коробки. Старый маршрут ниже
        // сохраняется, чтобы уже выпущенные сборки продолжали работать без изменений.
        endpoints.MapGet("/api/inventory/boxes", async (string? qrCode, BoxRepository boxes) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите QR-код коробки." });

            var box = await boxes.FindByQrCodeAsync(qrCode);
            if (box is null)
                return Results.NotFound(new { success = false, message = "Коробка с таким QR-кодом не найдена." });

            var (boxId, qrCodeId, title, normalizedCode, hasPhoto, contentType) = box.Value;
            var items = await boxes.GetChecklistItemsAsync(boxId);
            var photos = await boxes.GetBoxPhotosAsync(boxId, normalizedCode);
            var modelAvailable = await boxes.HasBoxModelAsync(boxId);

            if (photos.Count == 0 && hasPhoto)
                photos.Add(new
                {
                    pageNumber = 1,
                    caption = "Вид содержимого 1",
                    contentType,
                    photoUrl = $"/api/inventory/box-photo?qrCode={Uri.EscapeDataString(normalizedCode)}&pageNumber=1"
                });

            return Results.Ok(new
            {
                success = true,
                boxId,
                qrCodeId,
                qrCode = normalizedCode,
                title,
                photoAvailable = photos.Count > 0,
                photos,
                modelAvailable,
                modelUrl = modelAvailable ? $"/api/inventory/box-model?qrCode={Uri.EscapeDataString(normalizedCode)}" : null,
                items
            });
        })
        .WithTags("Inventory")
        .WithSummary("Получить содержимое коробки по QR-коду")
        .Produces(200).Produces(400).Produces(404);

        endpoints.MapGet("/api/inventory/box-photo", async (string? qrCode, int? pageNumber, BoxRepository boxes) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите QR-код коробки." });
            var image = await boxes.GetBoxPhotoAsync(qrCode, Math.Max(pageNumber ?? 1, 1));
            return image is null
                ? Results.NotFound(new { success = false, message = "Изображение содержимого коробки не найдено." })
                : Results.Bytes(image.Value.bytes, image.Value.contentType);
        })
        .WithTags("Inventory")
        .WithSummary("Получить изображение содержимого коробки")
        .Produces(200).Produces(404);

        endpoints.MapGet("/api/inventory/box-model", async (string? qrCode, BoxRepository boxes) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите QR-код коробки." });
            var model = await boxes.GetBoxModelAsync(qrCode);
            return model is null
                ? Results.NotFound(new { success = false, message = "Для этой коробки GLB-модель ещё не загружена." })
                : Results.Bytes(model.Value.data, model.Value.contentType, fileDownloadName: model.Value.fileName);
        })
        .WithTags("Inventory")
        .WithSummary("Получить GLB-модель содержимого коробки")
        .Produces(200).Produces(404);

        endpoints.MapGet("/api/inventory/assignments/{userId:long}/active", async (long userId, InventoryRunRepository runs) =>
        {
            if (userId <= 0)
                return Results.BadRequest(new { success = false, message = "Некорректный идентификатор сотрудника." });
            var assignment = await runs.GetActiveAssignmentForUserAsync(userId);
            return assignment is null
                ? Results.NotFound(new { success = false, message = "Администратор пока не назначил вам инвентаризацию коробок." })
                : Results.Ok(assignment);
        })
        .WithTags("Inventory")
        .WithSummary("Получить активное назначение инвентаризации сотрудника")
        .Produces(200).Produces(404);

        endpoints.MapGet("/api/inventory/assignments/{userId:long}/overview", async (long userId, InventoryRunRepository runs) =>
        {
            if (userId <= 0)
                return Results.BadRequest(new { success = false, message = "Некорректный идентификатор сотрудника." });

            var overview = await runs.GetAssignmentOverviewForUserAsync(userId);
            return Results.Ok(overview);
        })
        .WithTags("Inventory")
        .WithSummary("Получить активное назначение и историю сотрудника")
        .Produces<Contracts.InventoryAssignmentOverviewDto>(200).Produces(400);

        // Старые маршруты и имя cabinetId оставлены только для уже собранных клиентов.
        endpoints.MapGet("/api/inventory/cabinets", async (string? qrCode, BoxRepository boxes) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите qrCode" });

            var box = await boxes.FindByQrCodeAsync(qrCode);
            if (box is null)
                return Results.NotFound(new { success = false, message = "Коробка с таким QR-кодом не найдена." });

            var (boxId, qrCodeId, title, normalizedCode, hasPhoto, contentType) = box.Value;
            var items = await boxes.GetChecklistItemsAsync(boxId);
            var photos = await boxes.GetBoxPhotosAsync(boxId, normalizedCode);

            if (photos.Count == 0 && hasPhoto)
            {
                photos.Add(new
                {
                    pageNumber = 1,
                    caption = "Лист 1",
                    contentType,
                    photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(normalizedCode)}&pageNumber=1"
                });
            }
            var finalHasPhoto = photos.Count > 0;

            return Results.Ok(new
            {
                success = true,
                cabinetId = boxId,
                qrCodeId,
                qrCode = normalizedCode,
                title,
                photoAvailable = finalHasPhoto,
                photoContentType = contentType,
                photoUrl = finalHasPhoto ? $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(normalizedCode)}&pageNumber=1" : null,
                photos,
                items
            });
        })
        .WithTags("Inventory")
        .WithSummary("Старый маршрут: получить коробку и перечень по QR-коду")
        .Produces(200).Produces(400).Produces(404);

        endpoints.MapGet("/api/inventory/cabinet-photo", async (string? qrCode, int? pageNumber, BoxRepository boxes) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите qrCode" });

            int page = Math.Max(pageNumber ?? 1, 1);
            var photoData = await boxes.GetBoxPhotoAsync(qrCode, page);

            if (photoData is null)
                return Results.NotFound(new { success = false, message = "Изображение содержимого коробки не найдено." });

            return Results.Bytes(photoData.Value.bytes, photoData.Value.contentType, enableRangeProcessing: false);
        })
        .WithTags("Inventory")
        .WithSummary("Старый маршрут: получить изображение коробки")
        .Produces(200).Produces(400).Produces(404);

        endpoints.MapPost("/api/sync/inventory-runs", async (Contracts.InventoryRunSyncPayload payload, InventorySyncService syncService) =>
        {
            if (payload?.items == null)
                return Results.BadRequest(new { success = false, message = "Список проверок отсутствует" });

            var result = await syncService.ProcessInventoryRunsAsync(payload);
            return Results.Ok(new { result.imported, result.duplicates, result.skipped, result.errors });
        })
        .WithTags("Inventory")
        .WithSummary("Синхронизировать завершённые инвентаризации")
        .WithDescription("Принимает сохранённые офлайн-проверки. Для отчёта с вложениями нужна хотя бы одна фотография; голосовой комментарий можно не записывать. Повторная отправка безопасна благодаря clientRunId.")
        .Produces(200).Produces(400);

        return endpoints;
    }
}

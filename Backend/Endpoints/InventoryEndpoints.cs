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
        endpoints.MapGet("/api/inventory/cabinets", async (string? qrCode, CabinetRepository cabinets) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите qrCode" });

            var cabinet = await cabinets.FindByQrCodeAsync(qrCode);
            if (cabinet is null)
                return Results.NotFound(new { success = false, message = "Шкаф для этого QR-кода не найден" });

            var (cabinetId, qrCodeId, title, normalizedCode, hasPhoto, contentType) = cabinet.Value;
            var items = await cabinets.GetChecklistItemsAsync(cabinetId);
            var photos = await cabinets.GetCabinetPhotosAsync(cabinetId, normalizedCode);

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
                cabinetId,
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
        .WithSummary("Получить шкаф и перечень по QR-коду")
        .Produces(200).Produces(400).Produces(404);

        endpoints.MapGet("/api/inventory/cabinet-photo", async (string? qrCode, int? pageNumber, CabinetRepository cabinets) =>
        {
            if (string.IsNullOrWhiteSpace(qrCode))
                return Results.BadRequest(new { success = false, message = "Укажите qrCode" });

            int page = Math.Clamp(pageNumber ?? 1, 1, 2);
            var photoData = await cabinets.GetCabinetPhotoAsync(qrCode, page);

            if (photoData is null)
                return Results.NotFound(new { success = false, message = "Фотография шкафа не найдена" });

            return Results.Bytes(photoData.Value.bytes, photoData.Value.contentType, enableRangeProcessing: false);
        })
        .WithTags("Inventory")
        .WithSummary("Получить фотографию шкафа")
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

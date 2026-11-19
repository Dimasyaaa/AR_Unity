using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Contracts;
using Backend.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Админские эндпоинты для управления шкафами и просмотра отчётов инвентаризации.
/// </summary>
public static class AdminInventoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/inventory/cabinets", async (CabinetRepository cabinets) =>
        {
            var list = await cabinets.GetAllCabinetsAsync();
            return Results.Ok(list);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Список QR-кодов и настроенных шкафов")
        .Produces(200);

        endpoints.MapGet("/api/admin/inventory/cabinets/{qrCodeId:long}", async (long qrCodeId, CabinetRepository cabinets) =>
        {
            var cabinet = await cabinets.GetCabinetByQrCodeIdAsync(qrCodeId);
            if (cabinet is null) return Results.NotFound(new { message = "QR-код не найден" });

            var (cabinetId, code, objectName, title, photoAvailable) = cabinet.Value;
            var items = cabinetId > 0 ? await cabinets.GetChecklistItemsAsync(cabinetId) : new List<object>();
            
            var photos = new List<object>();
            if (cabinetId > 0)
            {
                photos = await cabinets.GetCabinetPhotosAsync(cabinetId, code);
            }
            if (photos.Count == 0 && photoAvailable)
            {
                photos.Add(new { pageNumber = 1, caption = "Лист 1", contentType = "image/jpeg", photoUrl = $"/api/inventory/cabinet-photo?qrCode={Uri.EscapeDataString(code)}&pageNumber=1" });
            }

            return Results.Ok(new { qrCodeId, qrCode = code, objectName, cabinetId, title, photoAvailable, photos, items });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Получить настройки шкафа и его чек-лист")
        .Produces(200).Produces(404);

        endpoints.MapPut("/api/admin/inventory/cabinets/{qrCodeId:long}", async (long qrCodeId, AdminCabinetDto dto, CabinetRepository cabinets) =>
        {
            if (string.IsNullOrWhiteSpace(dto.title) || dto.items == null)
                return Results.BadRequest(new { message = "Название шкафа и список предметов обязательны" });

            var cleanedItems = dto.items.Select((item, index) => new AdminChecklistItemDto
            {
                id = item.id,
                name = item.name?.Trim() ?? string.Empty,
                description = item.description?.Trim() ?? string.Empty,
                sortOrder = item.sortOrder == 0 ? (index + 1) * 10 : item.sortOrder
            }).ToList();

            if (cleanedItems.Any(item => string.IsNullOrWhiteSpace(item.name)))
                return Results.BadRequest(new { message = "У каждого пункта должно быть название" });
            if (cleanedItems.GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return Results.BadRequest(new { message = "Названия предметов в одном шкафу не должны повторяться" });

            var photoUploads = new List<(int page, byte[] bytes, string type, string caption)>();
            var requestedPhotos = dto.photos ?? new List<AdminCabinetPhotoDto>();
            
            if (requestedPhotos.GroupBy(p => p.pageNumber).Any(g => g.Count() > 1) || requestedPhotos.Any(p => p.pageNumber is < 1 or > 2))
                return Results.BadRequest(new { message = "Можно загрузить не более двух фотографий: лист 1 и лист 2" });

            foreach (var req in requestedPhotos)
            {
                if (string.IsNullOrWhiteSpace(req.imageBase64)) continue;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(req.imageBase64); }
                catch (FormatException) { return Results.BadRequest(new { message = "Файл фотографии повреждён" }); }
                
                if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
                    return Results.BadRequest(new { message = "Каждая фотография должна быть не больше 8 МБ" });
                if (req.contentType is not ("image/jpeg" or "image/png" or "image/webp"))
                    return Results.BadRequest(new { message = "Поддерживаются JPEG, PNG и WebP" });

                photoUploads.Add((req.pageNumber, bytes, req.contentType!, string.IsNullOrWhiteSpace(req.caption) ? $"Лист {req.pageNumber}" : req.caption.Trim()));
            }

            if (photoUploads.Count == 0 && !string.IsNullOrWhiteSpace(dto.photoBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(dto.photoBase64);
                    if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
                        return Results.BadRequest(new { message = "Фотография должна быть не больше 8 МБ" });
                    if (dto.photoContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                        return Results.BadRequest(new { message = "Поддерживаются JPEG, PNG и WebP" });
                    photoUploads.Add((1, bytes, dto.photoContentType!, "Лист 1"));
                }
                catch (FormatException) { return Results.BadRequest(new { message = "Файл фотографии повреждён" }); }
            }

            var removalPages = (dto.removePhotoPages ?? new List<int>()).Distinct().ToArray();
            if (removalPages.Any(page => page is < 1 or > 2))
                return Results.BadRequest(new { message = "Укажите номер фотографии: 1 или 2" });

            try
            {
                var result = await cabinets.UpsertCabinetAsync(qrCodeId, dto.title.Trim(), cleanedItems, photoUploads, removalPages);
                if (result is null)
                    return Results.NotFound(new { message = "QR-код не найден" });

                return Results.Ok(new { success = true, message = "Данные шкафа сохранены" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Создать или обновить карточку шкафа, фотографию и перечень")
        .Produces(200).Produces(400).Produces(404);

        endpoints.MapDelete("/api/admin/inventory/cabinets/{qrCodeId:long}", async (long qrCodeId, QrDeletionRepository qrDeletion) =>
        {
            var deletion = await qrDeletion.DeleteQrAndDependenciesAsync(qrCodeId, requireCabinet: true);
            if (!deletion.QrFound || !deletion.CabinetFound)
                return Results.NotFound(new { success = false, message = "Шкаф не найден" });

            return Results.Ok(new
            {
                success = true,
                message = "Шкаф, его QR-код и история удалены",
                deletion.DeletedRunItems,
                deletion.DeletedRuns,
                deletion.DeletedChecklistItems,
                deletion.DeletedPhotos,
                deletion.DeletedEvents
            });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Полностью удалить шкаф вместе с QR-кодом и историей")
        .Produces(200).Produces(404);

        // --- Эндпоинты просмотра отчётов ---
        endpoints.MapGet("/api/admin/inventory/runs", async (int? limit, InventoryRunRepository runRepo) =>
        {
            var runs = await runRepo.GetRunsAsync(limit ?? 100);
            return Results.Ok(runs);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Последние отправленные инвентаризации")
        .Produces(200);

        endpoints.MapGet("/api/admin/inventory/runs/{runId:long}", async (long runId, InventoryRunRepository runRepo) =>
        {
            var details = await runRepo.GetRunDetailsAsync(runId);
            return details is null ? Results.NotFound(new { message = "Инвентаризация не найдена" }) : Results.Ok(details);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Подробности выбранной инвентаризации")
        .Produces(200).Produces(404);

        endpoints.MapGet("/api/admin/inventory/runs/{runId:long}/media/photo/{index:int}", async (long runId, int index, HttpContext context, InventoryRunRepository runRepo) =>
        {
            if (index < 0 || index > 4) return Results.NotFound();
            var attachment = await runRepo.GetRunPhotoAsync(runId, index);
            if (attachment is null) return Results.NotFound();
            
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Bytes(attachment.Data, attachment.ContentType, enableRangeProcessing: true);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Фотография отчёта инвентаризации");

        endpoints.MapGet("/api/admin/inventory/runs/{runId:long}/media/{kind}", async (long runId, string kind, HttpContext context, InventoryRunRepository runRepo) =>
        {
            if (kind != "photo" && kind != "voice") return Results.NotFound();
            var attachment = await runRepo.GetRunMediaAsync(runId, kind);
            if (attachment is null) return Results.NotFound();
            
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Bytes(attachment.Data, attachment.ContentType, enableRangeProcessing: true);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Получить фото или голосовое сообщение инвентаризации")
        .Produces(200).Produces(404);

        return endpoints;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Backend.Contracts;
using Backend.Data;
using Backend.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Backend.Endpoints;

/// <summary>
/// Админские эндпоинты для управления коробками, назначениями и отчётами инвентаризации.
/// </summary>
public static class AdminInventoryEndpoints
{
    public static IEndpointRouteBuilder MapAdminInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/inventory/assignments", async (
            InventoryAssignmentCreateDto request, InventoryRunRepository runs) =>
        {
            if (request is null || request.userId <= 0 || request.qrCodeIds is null)
                return Results.BadRequest(new { success = false, message = "Выберите сотрудника и хотя бы одну коробку." });

            try
            {
                var assignment = await runs.CreateAssignmentAsync(request.userId, request.qrCodeIds);
                return Results.Ok(new
                {
                    success = true,
                    id = assignment.id,
                    clientBatchId = assignment.clientBatchId,
                    expectedBoxCount = request.qrCodeIds.Distinct().Count(),
                    message = $"Инвентаризация {request.qrCodeIds.Distinct().Count()} коробок назначена сотруднику."
                });
            }
            catch (InvalidOperationException error)
            {
                return Results.Conflict(new { success = false, message = error.Message });
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { success = false, message = error.Message });
            }
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Назначить сотруднику проверку выбранных коробок")
        .Produces(200).Produces(400).Produces(409);

        endpoints.MapGet("/api/admin/inventory/boxes", async (BoxRepository boxes) =>
        {
            var list = await boxes.GetAllBoxesAsync();
            return Results.Ok(list);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Список QR-кодов и настроенных коробок")
        .Produces(200);

        endpoints.MapGet("/api/admin/inventory/boxes/{qrCodeId:long}", async (long qrCodeId, BoxRepository boxes) =>
        {
            var box = await boxes.GetBoxByQrCodeIdAsync(qrCodeId);
            if (box is null) return Results.NotFound(new { message = "QR-код не найден" });

            var (boxId, code, objectName, title, photoAvailable, modelFileName) = box.Value;
            var items = boxId > 0 ? await boxes.GetChecklistItemsAsync(boxId) : new List<object>();
            
            var photos = new List<object>();
            if (boxId > 0)
            {
                photos = await boxes.GetBoxPhotosAsync(boxId, code);
            }
            if (photos.Count == 0 && photoAvailable)
            {
                photos.Add(new { pageNumber = 1, caption = "Содержимое коробки", contentType = "image/jpeg", photoUrl = $"/api/inventory/box-photo?qrCode={Uri.EscapeDataString(code)}&pageNumber=1" });
            }

            return Results.Ok(new { qrCodeId, qrCode = code, objectName, boxId, title, photoAvailable, modelAvailable = modelFileName != null, modelFileName, photos, items });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Получить данные коробки, изображения, GLB и перечень позиций")
        .Produces(200).Produces(404);

        endpoints.MapPut("/api/admin/inventory/boxes/{qrCodeId:long}", async (long qrCodeId, AdminBoxDto dto, BoxRepository boxes) =>
        {
            if (string.IsNullOrWhiteSpace(dto.title) || dto.items == null)
                return Results.BadRequest(new { message = "Название коробки и перечень позиций обязательны." });

            var cleanedItems = dto.items.Select((item, index) => new AdminChecklistItemDto
            {
                id = item.id,
                name = item.name?.Trim() ?? string.Empty,
                description = item.description?.Trim() ?? string.Empty,
                sortOrder = item.sortOrder == 0 ? (index + 1) * 10 : item.sortOrder,
                expectedQuantity = item.expectedQuantity,
                modelType = item.modelType
            }).ToList();

            if (cleanedItems.Any(item => string.IsNullOrWhiteSpace(item.name)))
                return Results.BadRequest(new { message = "У каждого пункта должно быть название" });
            if (cleanedItems.Any(item => item.expectedQuantity is < 1 or > 100000))
                return Results.BadRequest(new { message = "Учётное количество каждой позиции должно быть от 1 до 100000" });
            if (cleanedItems.GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return Results.BadRequest(new { message = "Названия позиций в одной коробке не должны повторяться." });

            var photoUploads = new List<(int page, byte[] bytes, string type, string caption)>();
            var requestedPhotos = dto.photos ?? new List<AdminBoxPhotoDto>();
            
            if (requestedPhotos.Any(p => p.pageNumber < 1) ||
                requestedPhotos.GroupBy(p => p.pageNumber).Any(g => g.Count() > 1))
                return Results.BadRequest(new { message = "Номер каждого изображения должен быть положительным и уникальным." });

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

                photoUploads.Add((req.pageNumber, bytes, req.contentType!, string.IsNullOrWhiteSpace(req.caption) ? $"Содержимое коробки · {req.pageNumber}" : req.caption.Trim()));
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
                    photoUploads.Add((1, bytes, dto.photoContentType!, "Содержимое коробки"));
                }
                catch (FormatException) { return Results.BadRequest(new { message = "Файл фотографии повреждён" }); }
            }

            var removalPages = (dto.removePhotoPages ?? new List<int>()).Distinct().ToArray();
            if (removalPages.Any(page => page < 1))
                return Results.BadRequest(new { message = "Номер изображения должен быть положительным." });

            byte[]? glbModel = null;
            string? modelFileName = null;
            if (!string.IsNullOrWhiteSpace(dto.modelBase64))
            {
                try { glbModel = Convert.FromBase64String(dto.modelBase64); }
                catch (FormatException) { return Results.BadRequest(new { message = "GLB-файл повреждён." }); }
                if (glbModel.Length == 0 || glbModel.Length > 20 * 1024 * 1024)
                    return Results.BadRequest(new { message = "Размер GLB-модели должен быть не больше 20 МБ." });
                if (!string.Equals(Path.GetExtension(dto.modelFileName), ".glb", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { message = "Поддерживаются 3D-модели только в формате GLB." });
                modelFileName = Path.GetFileName(dto.modelFileName);
            }

            try
            {
                var result = await boxes.UpsertBoxAsync(qrCodeId, dto.title.Trim(), cleanedItems,
                    photoUploads, removalPages, glbModel, modelFileName, dto.removeModel);
                if (result is null)
                    return Results.NotFound(new { message = "QR-код не найден" });

                return Results.Ok(new { success = true, message = "Данные коробки сохранены" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Создать или обновить коробку, изображения, GLB-модель и перечень")
        .Produces(200).Produces(400).Produces(404);

        endpoints.MapDelete("/api/admin/inventory/boxes/{qrCodeId:long}", async (long qrCodeId, QrDeletionRepository qrDeletion) =>
        {
            var deletion = await qrDeletion.DeleteQrAndDependenciesAsync(qrCodeId, requireBox: true);
            if (!deletion.QrFound || !deletion.BoxFound)
                return Results.NotFound(new { success = false, message = "Коробка не найдена." });

            return Results.Ok(new
            {
                success = true,
                message = "Коробка, её QR-код и история удалены.",
                deletion.DeletedRunItems,
                deletion.DeletedRuns,
                deletion.DeletedChecklistItems,
                deletion.DeletedPhotos,
                deletion.DeletedEvents
            });
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Полностью удалить коробку вместе с QR-кодом и историей")
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
            if (index < 0) return Results.NotFound();
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

        // Общие проверки связывают все коробки, которые сотрудник прошёл за один запуск.
        endpoints.MapGet("/api/admin/inventory/batches", async (int? limit, InventoryRunRepository runRepo) =>
        {
            var batches = await runRepo.GetBatchesAsync(limit ?? 100);
            return Results.Ok(batches);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Список общих инвентаризаций коробок")
        .Produces(200);

        endpoints.MapGet("/api/admin/inventory/batches/{batchId:long}/rows", async (long batchId, InventoryRunRepository runRepo) =>
        {
            var rows = await runRepo.GetBatchExportRowsAsync(batchId);
            return rows.Count == 0
                ? Results.NotFound(new { message = "Для этой общей проверки пока нет результатов" })
                : Results.Ok(rows);
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Сводная таблица позиций всех коробок общей проверки")
        .Produces(200).Produces(404);

        endpoints.MapGet("/api/admin/inventory/batches/{batchId:long}/excel", async (long batchId, InventoryRunRepository runRepo) =>
        {
            var rows = await runRepo.GetBatchExportRowsAsync(batchId);
            if (rows.Count == 0)
                return Results.NotFound(new { message = "Для этой общей проверки пока нет результатов" });

            byte[] workbook = InventoryBatchExcelWriter.Create(rows);
            return Results.File(workbook,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"inventory-batch-{batchId}.xlsx");
        })
        .WithTags("Admin: Inventory")
        .WithSummary("Скачать общую инвентаризацию коробок в формате Excel")
        .Produces(200).Produces(404);

        return endpoints;
    }
}

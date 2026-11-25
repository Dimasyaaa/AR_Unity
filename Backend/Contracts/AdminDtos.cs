namespace Backend.Contracts;

public class UserDto
{
    public string fullName { get; set; } = string.Empty;
    public string department { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    public bool isActive { get; set; } = true;
}

public class QrDto
{
    public string code { get; set; } = string.Empty;
    public string objectName { get; set; } = string.Empty;
    public bool isActive { get; set; } = true;
}

/// <summary>Данные для создания этикетки МТР с ЕНС/СКК и учётным количеством.</summary>
public sealed class InventoryQrCreateDto
{
    public string ensCode { get; set; } = string.Empty;
    public int accountingQuantity { get; set; }
}

public class SyncPayloadDto
{
    public List<SyncSessionDto> items { get; set; } = new();
}

public class SyncSessionDto
{
    public long userId { get; set; }
    public long qrCodeId { get; set; }
    public string action { get; set; } = string.Empty;
    public string comment { get; set; } = string.Empty;
    public string actionTime { get; set; } = string.Empty;
    public string fullName { get; set; } = string.Empty;
    public string department { get; set; } = string.Empty;
}

public class AdminBoxDto
{
    public string title { get; set; } = string.Empty;
    public string? photoBase64 { get; set; }
    public string? photoContentType { get; set; }
    public List<AdminBoxPhotoDto>? photos { get; set; }
    public List<int>? removePhotoPages { get; set; }
    public string? modelBase64 { get; set; }
    public string? modelFileName { get; set; }
    public bool removeModel { get; set; }
    public List<AdminChecklistItemDto> items { get; set; } = new();
}

public class AdminBoxPhotoDto
{
    public int pageNumber { get; set; }
    public string? caption { get; set; }
    public string? imageBase64 { get; set; }
    public string? contentType { get; set; }
}

public class AdminChecklistItemDto
{
    public long id { get; set; }
    public string? name { get; set; }
    public string? description { get; set; }
    public int sortOrder { get; set; }
    public int expectedQuantity { get; set; } = 1;
    public string modelType { get; set; } = "cube";
    public string? structuralDivision { get; set; }
    public string? warehouse { get; set; }
    public string? ensCode { get; set; }
    public string? accountCode { get; set; }
    public string? mol { get; set; }
    public string? storageLocation { get; set; }
    public string? unit { get; set; }
}

/// <summary>Пользователь и выбранные QR-коды для назначенной проверки.</summary>
public sealed class InventoryAssignmentCreateDto
{
    public long userId { get; set; }
    public List<long> qrCodeIds { get; set; } = new();
}

/// <summary>Назначенная пользователю коробка, возвращаемая мобильному приложению.</summary>
public sealed class AssignedBoxDto
{
    public long qrCodeId { get; set; }
    public string qrCode { get; set; } = string.Empty;
    public string title { get; set; } = string.Empty;
}

/// <summary>Назначение выбранных администратором коробок.</summary>
public sealed class InventoryAssignmentDto
{
    public bool success { get; set; } = true;
    public string clientBatchId { get; set; } = string.Empty;
    public int expectedBoxCount { get; set; }
    public string status { get; set; } = string.Empty;
    public List<AssignedBoxDto> boxes { get; set; } = new();
}

/// <summary>Сводка общей инвентаризации, уже завершённой сотрудником.</summary>
public sealed class InventoryBatchHistoryDto
{
    public string clientBatchId { get; set; } = string.Empty;
    public int expectedBoxCount { get; set; }
    public int completedBoxCount { get; set; }
    public string status { get; set; } = string.Empty;
    public DateTimeOffset startedAt { get; set; }
    public DateTimeOffset? completedAt { get; set; }
}

/// <summary>Активное назначение и история сотрудника для экрана выбора инвентаризации.</summary>
public sealed class InventoryAssignmentOverviewDto
{
    public InventoryAssignmentDto? activeAssignment { get; set; }
    public List<InventoryBatchHistoryDto> history { get; set; } = new();
}

public class InventoryRunSyncPayload
{
    public List<InventoryRunSyncItem> items { get; set; } = new();
}

public class InventoryRunSyncItem
{
    public Guid clientRunId { get; set; }
    public Guid? clientBatchId { get; set; }
    public int? expectedBoxCount { get; set; }
    public long userId { get; set; }
    public string qrCode { get; set; } = string.Empty;
    public string startedAt { get; set; } = string.Empty;
    public string completedAt { get; set; } = string.Empty;
    public List<InventoryRunSyncResult> items { get; set; } = new();
    public string? photoBase64 { get; set; }
    public List<string>? photosBase64 { get; set; }
    public string? voiceBase64 { get; set; }
    public string? reportNote { get; set; }
}

public class InventoryRunSyncResult
{
    public long itemId { get; set; }
    public string status { get; set; } = string.Empty;
    public int? actualQuantity { get; set; }
    public string? comment { get; set; }
}

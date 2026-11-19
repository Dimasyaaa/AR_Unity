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

public class AdminCabinetDto
{
    public string title { get; set; } = string.Empty;
    public string? photoBase64 { get; set; }
    public string? photoContentType { get; set; }
    public List<AdminCabinetPhotoDto>? photos { get; set; }
    public List<int>? removePhotoPages { get; set; }
    public List<AdminChecklistItemDto> items { get; set; } = new();
}

public class AdminCabinetPhotoDto
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
}

public class InventoryRunSyncPayload
{
    public List<InventoryRunSyncItem> items { get; set; } = new();
}

public class InventoryRunSyncItem
{
    public Guid clientRunId { get; set; }
    public long userId { get; set; }
    public string qrCode { get; set; } = string.Empty;
    public string startedAt { get; set; } = string.Empty;
    public string completedAt { get; set; } = string.Empty;
    public List<InventoryRunSyncResult> items { get; set; } = new();
    public string? photoBase64 { get; set; }
    public List<string>? photosBase64 { get; set; }
    public string? voiceBase64 { get; set; }
}

public class InventoryRunSyncResult
{
    public long itemId { get; set; }
    public string status { get; set; } = string.Empty;
    public string? comment { get; set; }
}
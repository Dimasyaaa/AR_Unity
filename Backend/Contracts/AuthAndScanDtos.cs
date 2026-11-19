namespace Backend.Contracts;

public class LoginRequest
{
    public string fullName { get; set; } = string.Empty;
    public string department { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
}

public class ScanRequest
{
    public long userId { get; set; }
    public string qrCode { get; set; } = string.Empty;
}
namespace Backend.Helpers;

public static class TimeHelper
{
    public static DateTimeOffset ParseTime(string? value, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : fallback;
}
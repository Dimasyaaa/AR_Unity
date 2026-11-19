namespace Backend.Helpers;

public static class QrHelper
{
    public static IEnumerable<string> ExpandQr(string value)
    {
        var trimmed = value.Trim();
        yield return trimmed;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) yield break;

        var tail = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "");
        if (!string.IsNullOrWhiteSpace(tail) && !string.Equals(tail, trimmed, StringComparison.Ordinal))
            yield return tail;
    }
}
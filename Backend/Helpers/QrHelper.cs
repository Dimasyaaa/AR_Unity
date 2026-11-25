namespace Backend.Helpers;

public static class QrHelper
{
    /// <summary>Проверяет, что QR содержит ЕНС/СКК и положительное учётное количество.</summary>
    public static bool TryParseInventoryPayload(string value, out string ensCode, out int accountingQuantity)
    {
        ensCode = string.Empty;
        accountingQuantity = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] parts = value.Trim().Split('#');
        if (parts.Length != 2) return false;

        string parsedEns = parts[0].Trim();
        if (parsedEns.Length is < 1 or > 100 || parsedEns.Any(char.IsControl)) return false;
        if (!int.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int parsedQuantity) ||
            parsedQuantity is < 1 or > 100000)
            return false;

        ensCode = parsedEns;
        accountingQuantity = parsedQuantity;
        return true;
    }

    /// <summary>Собирает единый текст QR-этикетки из ЕНС/СКК и учётного количества.</summary>
    public static string CreateInventoryPayload(string ensCode, int accountingQuantity)
    {
        string normalizedEns = ensCode?.Trim() ?? string.Empty;
        if (normalizedEns.Length is < 1 or > 100 || normalizedEns.Contains('#') || normalizedEns.Any(char.IsControl))
            throw new ArgumentException("ЕНС/СКК должен содержать от 1 до 100 символов и не содержать символ #.", nameof(ensCode));
        if (accountingQuantity is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(accountingQuantity), "Учётное количество должно быть от 1 до 100000.");

        return $"{normalizedEns}#{accountingQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

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

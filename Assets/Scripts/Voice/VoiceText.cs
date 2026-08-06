public static class VoiceText
{
    public static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.ToLower()
                .Replace('ё', 'е')
                .Replace(',', ' ').Replace('.', ' ')
                .Replace('!', ' ').Replace('?', ' ')
                .Replace(':', ' ').Replace(';', ' ')
                .Replace('-', ' ')
                .Trim();
    }

    public static bool Has(string normalized, params string[] words)
    {
        foreach (var w in words)
            if (normalized.Contains(w)) return true;
        return false;
    }
}
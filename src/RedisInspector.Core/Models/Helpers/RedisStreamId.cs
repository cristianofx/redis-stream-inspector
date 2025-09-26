using System.Globalization;

public static class RedisStreamId
{
    /// <summary>
    /// Converts a Redis Stream ID ("ms-seq") to a UTC DateTimeOffset.
    /// Returns null for special IDs like "-" or "+".
    /// </summary>
    public static DateTimeOffset? ToUtc(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id == "-" || id == "+") return null;

        var dash = id.IndexOf('-');
        if (dash <= 0) return null;

        var msPart = id.AsSpan(0, dash);
        if (!long.TryParse(msPart, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            return null;

        // Guard against out-of-range values
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convenience for local time.
    /// </summary>
    public static DateTime? ToLocal(string id)
        => ToUtc(id)?.LocalDateTime;

    /// <summary>
    /// Convenience for local time.
    /// </summary>
    public static DateTime? ToUtcDateTime(string id)
        => ToUtc(id)?.UtcDateTime;
}

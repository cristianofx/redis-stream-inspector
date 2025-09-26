namespace RedisInspector.Core.Models.Helpers
{
    /// <summary>
    /// A single matched entry.
    /// </summary>
    public sealed class SearchHit
    {
        public string Stream { get; init; } = "";
        public string Id { get; init; } = "";
        private string? _idDateTimeFormated;
        public string? IdDateTimeFormated
        {
            get => $"{Id} - {RedisStreamId.ToUtcDateTime(Id)?.ToString("yyyy-MM-dd HH:mm:ss.fff")}";
        }
        /// <summary>Flattened stream fields (as stored in Redis).</summary>
        public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
        /// <summary>Raw JSON extracted from JsonField (unescaped, no \u0022), if available.</summary>
        public string? RawMessage { get; init; }
    }
}

namespace RedisInspector.Core.Models.Helpers
{
    /// <summary>
    /// Options for stream search (shared by CLI and UI).
    /// </summary>
    public sealed class SearchOptions
    {
        /// <summary>false = head->tail (ascending), true = tail->head (descending).</summary>
        public bool NewestFirst { get; set; } // 
        /// <summary>Stream keys or glob patterns (e.g., "orders*", "AssetOutputStream").</summary>
        public List<string> Streams { get; set; } = new();

        /// <summary>Field name to look for (inside JSON payload or, optionally, direct fields if extended later).</summary>
        public string? FindField { get; set; }

        /// <summary>Value to compare with (equals). If null, only existence of the field is matched.</summary>
        public string? FindEq { get; set; }

        /// <summary>Inclusive start id for forward scans. Defaults to "-".</summary>
        public string FindFromId { get; set; } = "-";

        /// <summary>Inclusive end id for forward scans. Defaults to "+".</summary>
        public string FindToId { get; set; } = "+";

        /// <summary>Tail scan: reverse-scan the last N entries per stream. If &gt; 0, overrides forward scan.</summary>
        public int FindLast { get; set; } = 0;

        /// <summary>Upper bound on total matches to return across all streams.</summary>
        public int FindMax { get; set; } = int.MaxValue;

        /// <summary>Page size for XRANGE/XREVRANGE.</summary>
        public int FindPage { get; set; } = 100;

        /// <summary>Case-insensitive comparison for field names and equality checks.</summary>
        public bool FindCaseInsensitive { get; set; } = false;

        /// <summary>Stream field that contains the JSON payload.</summary>
        public string JsonField { get; set; } = "message";

        /// <summary>Optional dotted path inside the JSON payload (e.g., "a.b[0].c").</summary>
        public string? JsonPath { get; set; }

        /// <summary>If true, only emit the raw JSON from JsonField (RawMessage) for each hit.</summary>
        public bool MessageOnly { get; set; } = false;
    }
}

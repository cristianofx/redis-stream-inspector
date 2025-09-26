using System.Runtime.CompilerServices;
using System.Text.Json;
using RedisInspector.Core.Models.Helpers;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace RedisInspector.CLI.src.RedisInspector.Core.Services
{
    /// <summary>
    /// Stream search service usable from both CLI and UI.
    /// Construct with an existing multiplexer & database; call RunAsync to stream hits.
    /// </summary>
    public sealed class SearchRunner
    {
        private readonly IConnectionMultiplexer _mux;
        private readonly IDatabase _db;
        private readonly SearchOptions _opts;

        public SearchRunner(IConnectionMultiplexer mux, IDatabase db, SearchOptions options)
        {
            _mux = mux ?? throw new System.ArgumentNullException(nameof(mux));
            _db = db ?? throw new System.ArgumentNullException(nameof(db));
            _opts = options ?? throw new System.ArgumentNullException(nameof(options));
        }

        /// <summary>Executes the search and yields matches as they are found.</summary>
        public async IAsyncEnumerable<SearchHit> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var streams = await ResolveStreamsAsync(_mux, _db, _opts.Streams, ct).ConfigureAwait(false);
            if (streams.Count == 0)
                throw new NoStreamsFoundException(
                    _opts.Streams.Count == 1
                        ? $"Stream '{_opts.Streams[0]}' was not found."
                        : $"No streams were found matching: {string.Join(", ", _opts.Streams)}");

            var cmp = _opts.FindCaseInsensitive ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
            var remaining = _opts.FindMax;

            foreach (var stream in streams)
            {
                Console.WriteLine($"+++++ Fetching message from stream {stream.ToString()} ++++++");
                Console.WriteLine($"");
                if (remaining <= 0) yield break;
                ct.ThrowIfCancellationRequested();

                if (_opts.FindLast > 0)
                {
                    // Tail scan: last N per stream
                    int scanned = 0;
                    string max = "+";
                    string? lastBoundaryId = null;

                    while (scanned < _opts.FindLast && remaining > 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        int pageCount = System.Math.Min(_opts.FindPage, _opts.FindLast - scanned);
                        var page = await _db.StreamRangeAsync(stream, minId: "-", maxId: max, count: pageCount, messageOrder: Order.Descending)
                                            .ConfigureAwait(false);
                        if (page is null || page.Length == 0) break;

                        foreach (var entry in page)
                        {
                            if (lastBoundaryId != null && entry.Id == lastBoundaryId) continue; // de-dup
                            scanned++;

                            if (Matches(entry, cmp, out string? raw))
                            {
                                var hit = ToHit(stream, entry, raw);
                                yield return hit;
                                remaining--;
                                if (remaining <= 0) yield break;
                            }

                            if (scanned >= _opts.FindLast) break;
                        }

                        lastBoundaryId = page[^1].Id; // oldest in this page
                        max = lastBoundaryId;         // continue below this
                        if (page.Length < pageCount) break; // nothing more
                    }
                }
                else
                {
                    // Forward scan in range: XRANGE [from..to]
                    string from = string.IsNullOrWhiteSpace(_opts.FindFromId) ? "-" : _opts.FindFromId;
                    string to = string.IsNullOrWhiteSpace(_opts.FindToId) ? "+" : _opts.FindToId;
                    string? lastId = null;

                    while (remaining > 0)
                    {
                        ct.ThrowIfCancellationRequested();

                        var page = await _db.StreamRangeAsync(stream, minId: from, maxId: to, count: _opts.FindPage, messageOrder: Order.Ascending)
                                            .ConfigureAwait(false);
                        if (page is null || page.Length == 0) break;

                        foreach (var entry in page)
                        {
                            if (lastId != null && entry.Id == lastId) continue; // de-dup

                            if (Matches(entry, cmp, out string? raw))
                            {
                                var hit = ToHit(stream, entry, raw);
                                yield return hit;
                                remaining--;
                                if (remaining <= 0) yield break;
                            }
                        }

                        lastId = page[^1].Id;
                        from = lastId; // inclusive; next loop skips duplicate
                        if (page.Length < _opts.FindPage) break;
                    }
                }
            }
        }

        // ---------- Matching & extraction ----------

        // 1) Look inside JsonField (default "message"), which may be direct JSON or a JSON string containing JSON.
        // 2) If JsonPath provided, match at that path; otherwise, search for FindField anywhere in payload.
        // 3) If matched, optionally extract a RawMessage (unescaped, GetRawText()) for UI display.
        private bool Matches(StreamEntry entry, System.StringComparison cmp, out string? rawMessage)
        {
            rawMessage = null;
            var jsonFieldName = _opts.JsonField ?? "message";

            foreach (var nv in entry.Values)
            {
                if (!nv.Name.ToString().Equals(jsonFieldName, cmp)) continue;
                var s = nv.Value.ToString();
                if (string.IsNullOrWhiteSpace(s)) continue;

                if (!TryExtractJsonRoot(s, out var root))
                {
                    // Try secondary decode if it was a JSON string literal
                    try
                    {
                        using var outer = JsonDocument.Parse(s);
                        if (outer.RootElement.ValueKind == JsonValueKind.String)
                        {
                            var inner = outer.RootElement.GetString();
                            if (!string.IsNullOrWhiteSpace(inner) && TryExtractJsonRoot(inner!, out root))
                            {
                                // proceed
                            }
                            else
                            {
                                // plain string case: only match if FindField equals the whole string
                                if (_opts.FindField is not null &&
                                    (string.IsNullOrEmpty(_opts.FindEq) || string.Equals(inner, _opts.FindEq, cmp)))
                                {
                                    rawMessage = inner;
                                    return true;
                                }
                                // FindField optional, if null, always return message
                                else if (string.IsNullOrEmpty(_opts.FindField))
                                {
                                    rawMessage = inner;
                                    return true;
                                }
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                // We have a JSON root
                string? extracted = null;
                if (!string.IsNullOrEmpty(_opts.JsonPath))
                {
                    if (TryGetByPath(root, _opts.JsonPath!, out var elem))
                        extracted = JsonElementToComparableString(elem);
                }
                else if (!string.IsNullOrEmpty(_opts.FindField))
                {
                    if (TryFindFirstByKey(root, _opts.FindField!, cmp, out var elem))
                        extracted = JsonElementToComparableString(elem);
                }
                else if (string.IsNullOrEmpty(_opts.FindField))
                {
                        extracted = JsonElementToComparableString(root);
                }

                if (extracted != null)
                {
                    if (string.IsNullOrEmpty(_opts.FindField) && !string.IsNullOrEmpty(_opts.FindEq))
                    {
                        bool eqContains = extracted.Contains(_opts.FindEq);
                        if (eqContains)
                        {
                            // For UI, provide raw JSON (unescaped, no \u0022) when requested
                            if (_opts.MessageOnly)
                                rawMessage = root.GetRawText();
                            else
                                rawMessage = TryGetRawMessage(root) ?? root.GetRawText();
                            return true;
                        }
                    }
                    else
                    {
                        bool eqOk = _opts.FindEq is null || string.Equals(extracted, _opts.FindEq, cmp);
                        if (eqOk)
                        {
                            // For UI, provide raw JSON (unescaped, no \u0022) when requested
                            if (_opts.MessageOnly)
                                rawMessage = root.GetRawText();
                            else
                                rawMessage = TryGetRawMessage(root) ?? root.GetRawText();
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string? TryGetRawMessage(JsonElement root)
        {
            // If the root is a string, return it; if it's an object/array, return its GetRawText().
            return root.ValueKind switch
            {
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Object or JsonValueKind.Array => root.GetRawText(),
                _ => null
            };
        }

        private static bool TryGetByPath(JsonElement root, string path, out JsonElement result)
        {
            // dotted path: a.b[0].c
            var cur = root;
            var segs = path.Split('.', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            foreach (var raw in segs)
            {
                string name = raw;
                int? arrIndex = null;
                var lb = raw.IndexOf('[');
                if (lb >= 0 && raw.EndsWith("]") && int.TryParse(raw.AsSpan(lb + 1, raw.Length - lb - 2), out var idx))
                {
                    name = raw.Substring(0, lb);
                    arrIndex = idx;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(name, out cur))
                    { result = default; return false; }
                }

                if (arrIndex.HasValue)
                {
                    if (cur.ValueKind != JsonValueKind.Array || arrIndex.Value < 0 || arrIndex.Value >= cur.GetArrayLength())
                    { result = default; return false; }
                    cur = cur[arrIndex.Value];
                }
            }
            result = cur; return true;
        }

        private static bool TryFindFirstByKey(JsonElement root, string key, System.StringComparison cmp, out JsonElement result)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Equals(key, cmp)) { result = prop.Value; return true; }
                    if (TryFindFirstByKey(prop.Value, key, cmp, out result)) return true;
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    if (TryFindFirstByKey(item, key, cmp, out result)) return true;
            }
            result = default; return false;
        }

        private static string JsonElementToComparableString(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? string.Empty,
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => e.GetRawText()
        };

        // ---------- Stream & server helpers ----------

        private static async Task<List<RedisKey>> ResolveStreamsAsync(
            IConnectionMultiplexer mux, IDatabase db, List<string> inputs, CancellationToken ct)
        {
            var server = GetFirstServer(mux);
            var keys = new HashSet<RedisKey>();
            foreach (var item in inputs)
            {
                ct.ThrowIfCancellationRequested();

                if (item.IndexOfAny(new[] { '*', '?', '[' }) >= 0)
                {
                    await foreach (var k in server.KeysAsync(pattern: item, pageSize: 1000).WithCancellation(ct))
                    {
                        var type = await db.ExecuteAsync("TYPE", k).ConfigureAwait(false);
                        if (type.ToString() == "stream") keys.Add(k);
                    }
                }
                else
                {
                    var type = await db.ExecuteAsync("TYPE", item).ConfigureAwait(false);
                    if (type.ToString() == "stream") keys.Add(item);
                }
            }
            return new List<RedisKey>(keys);
        }

        private static IServer GetFirstServer(IConnectionMultiplexer mux)
        {
            foreach (var ep in mux.GetEndPoints())
            {
                var s = mux.GetServer(ep);
                if (s.IsConnected) return s;
            }
            throw new System.InvalidOperationException("No connected Redis server endpoint.");
        }

        private static SearchHit ToHit(RedisKey stream, StreamEntry entry, string? rawMessage)
        {
            var dict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var nv in entry.Values) dict[nv.Name.ToString()] = nv.Value.ToString();
            return new SearchHit
            {
                Stream = stream.ToString(),
                Id = entry.Id,
                Fields = dict,
                RawMessage = rawMessage
            };
        }

        private static bool TryExtractJsonRoot(string value, out JsonElement root)
        {
            try
            {
                var s = value.Trim();
                if (s.Length == 0) { root = default; return false; }

                // Case 1: direct JSON
                if (s[0] == '{' || s[0] == '[')
                {
                    using var doc = JsonDocument.Parse(s);
                    root = doc.RootElement.Clone();
                    return true;
                }
                // Case 2: JSON string literal that itself contains JSON
                if (s[0] == '"')
                {
                    using var outer = JsonDocument.Parse(s);
                    if (outer.RootElement.ValueKind == JsonValueKind.String)
                    {
                        var inner = outer.RootElement.GetString();
                        if (!string.IsNullOrWhiteSpace(inner))
                        {
                            var t = inner!.TrimStart();
                            if (t.StartsWith("{") || t.StartsWith("["))
                            {
                                using var innerDoc = JsonDocument.Parse(inner);
                                root = innerDoc.RootElement.Clone();
                                return true;
                            }
                        }
                    }
                }
            }
            catch { /* swallow parse errors; treat as not JSON */ }
            root = default; return false;
        }
    }

    public sealed class NoStreamsFoundException : Exception
    {
        public NoStreamsFoundException(string message) : base(message) { }
    }
}
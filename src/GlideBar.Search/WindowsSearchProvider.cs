using System.Data.OleDb;
using System.Text;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Search;

/// <summary>
/// Default <see cref="ISearchProvider"/>. Queries the Windows Search index through the
/// <c>Search.CollatorDSO</c> OLE DB provider, so GlideBar reuses the OS indexer instead of
/// maintaining its own always-on index.
/// </summary>
/// <remarks>
/// The CollatorDSO provider does not support OLE DB command parameters, so the query term is
/// escaped by hand (single quotes doubled; LIKE wildcards escaped) to avoid injection and quoting
/// bugs. The blocking OLE DB calls run on a thread-pool thread and honor the cancellation token.
/// </remarks>
public sealed class WindowsSearchProvider : ISearchProvider
{
    // Standard connection string for the Windows Search OLE DB provider.
    private const string ConnectionString =
        "provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    // Absolute ceiling on rows regardless of what a caller asks for.
    private const int HardCap = 60;

    private readonly Lazy<bool> _available;

    public WindowsSearchProvider()
        => _available = new Lazy<bool>(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Name => "Windows Search";

    /// <summary>True when the OLE DB provider and the indexer answer a trivial probe query.</summary>
    public bool IsAvailable => _available.Value;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int max, CancellationToken ct = default)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0 || !IsAvailable)
            return Array.Empty<SearchResult>();

        if (max <= 0) max = 30;
        max = Math.Min(max, HardCap);

        try
        {
            // Blocking OLE DB work off the calling (likely UI) thread.
            return await Task.Run(() => RunQuery(trimmed, max, ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<SearchResult>();
        }
        catch
        {
            // Never surface a search failure to the UI; just yield nothing.
            return Array.Empty<SearchResult>();
        }
    }

    private const string SelectCols =
        "System.ItemNameDisplay, System.ItemPathDisplay, System.DateModified, System.ItemType";

    private static List<SearchResult> RunQuery(string trimmed, int max, CancellationToken ct)
    {
        // Pull a larger candidate pool (most-recent first) so the ranker can promote better matches.
        var fetch = Math.Clamp(max * 2, max, HardCap);

        // Primary: index-seeking full-text word-prefix query. This uses the Windows Search index
        // instead of the old '%term%' LIKE, which forced a full index scan on every keystroke.
        var rows = TryExecute(BuildContainsSql(trimmed, fetch), ct);

        // Fallback: if the word-prefix match found nothing (e.g. a mid-word substring the FT engine
        // won't match), fall back to the slower substring LIKE so the user still gets results.
        if (rows is null || rows.Count == 0)
            rows = TryExecute(BuildLikeSql(trimmed, fetch), ct) ?? new();

        return SearchRanking.Finalize(rows, trimmed, max);
    }

    private static List<(string, string, SearchItemKind, DateTimeOffset?)>? TryExecute(string? sql, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sql)) return null;
        try { return Execute(sql, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // malformed predicate, FT catalog missing, etc. — let the caller fall back
    }

    private static List<(string name, string path, SearchItemKind kind, DateTimeOffset? modified)> Execute(
        string sql, CancellationToken ct)
    {
        var rows = new List<(string, string, SearchItemKind, DateTimeOffset?)>();

        using var conn = new OleDbConnection(ConnectionString);
        conn.Open();
        ct.ThrowIfCancellationRequested();

        using var cmd = new OleDbCommand(sql, conn);
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best effort */ } });

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();

            var path = reader.IsDBNull(1) ? null : reader.GetValue(1) as string;
            if (string.IsNullOrEmpty(path)) continue;

            var name = reader.IsDBNull(0) ? null : reader.GetValue(0) as string;
            if (string.IsNullOrEmpty(name)) name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;

            DateTimeOffset? modified = null;
            if (!reader.IsDBNull(2) && reader.GetValue(2) is DateTime dm)
            {
                try { modified = new DateTimeOffset(dm); } catch { /* unrepresentable; skip */ }
            }

            var itemType = reader.IsDBNull(3) ? null : reader.GetValue(3) as string;
            var kind = SearchRanking.ClassifyKind(path, isFolderHint: null, itemType: itemType);

            rows.Add((name!, path!, kind, modified));
        }

        return rows;
    }

    /// <summary>Index-optimized query: each whitespace token becomes a word-prefix CONTAINS term.</summary>
    private static string? BuildContainsSql(string trimmed, int fetch)
    {
        var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>(tokens.Length);
        foreach (var tk in tokens)
        {
            var clean = SanitizeFtToken(tk);
            if (clean.Length == 0) continue;
            parts.Add($"\"{clean}*\"");
        }
        if (parts.Count == 0) return null;

        var expr = string.Join(" AND ", parts).Replace("'", "''");
        return $"SELECT TOP {fetch} {SelectCols} FROM SystemIndex " +
               $"WHERE SCOPE='file:' AND CONTAINS(System.FileName, '{expr}') " +
               "ORDER BY System.DateModified DESC";
    }

    /// <summary>Substring fallback (leading wildcard forces a scan, so only used when CONTAINS is empty).</summary>
    private static string BuildLikeSql(string trimmed, int fetch)
    {
        var (pattern, needsEscape) = EscapeForLike(trimmed);
        var escapeClause = needsEscape ? " ESCAPE '\\'" : string.Empty;
        return $"SELECT TOP {fetch} {SelectCols} FROM SystemIndex " +
               $"WHERE SCOPE='file:' AND System.ItemNameDisplay LIKE '%{pattern}%'{escapeClause} " +
               "ORDER BY System.DateModified DESC";
    }

    /// <summary>Strip full-text operator characters and quotes from a query token.</summary>
    private static string SanitizeFtToken(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (ch is '"' or '*' or '?' or '\'' or '(' or ')' or '{' or '}'
                   or '[' or ']' or '&' or '|' or '!' or '^' or '~' or ':' or '\\')
                continue;
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Escape a raw query term for inclusion inside a single-quoted SQL string literal used with LIKE.
    /// Single quotes are doubled (literal escaping); <c>% _ [</c> and <c>\</c> are backslash-escaped so
    /// the user's text is matched literally rather than as wildcards.
    /// </summary>
    private static (string pattern, bool needsEscape) EscapeForLike(string raw)
    {
        var sb = new StringBuilder(raw.Length + 8);
        var needsEscape = false;

        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '\'':
                    sb.Append("''");
                    break;
                case '%':
                case '_':
                case '[':
                    sb.Append('\\').Append(ch);
                    needsEscape = true;
                    break;
                case '\\':
                    sb.Append("\\\\");
                    needsEscape = true;
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return (sb.ToString(), needsEscape);
    }

    private static bool Probe()
    {
        try
        {
            using var conn = new OleDbConnection(ConnectionString);
            conn.Open();
            using var cmd = new OleDbCommand("SELECT TOP 1 System.ItemName FROM SystemIndex", conn);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return true;
        }
        catch
        {
            return false; // provider not registered, indexer off, etc.
        }
    }
}

using GlideBar.Core.Models;

namespace GlideBar.Search;

/// <summary>
/// Shared result scoring + ordering used by every <see cref="GlideBar.Core.Abstractions.ISearchProvider"/>
/// implementation so ranking stays consistent across Windows Search and Everything.
/// Rules: prefix/exact match beats substring; more recent <c>DateModified</c> gives a boost;
/// ties break on recency then name.
/// </summary>
internal static class SearchRanking
{
    // Executable / shortcut extensions map to SearchItemKind.App.
    private static readonly HashSet<string> AppExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".appref-ms", ".msi", ".url", ".bat", ".cmd",
    };

    /// <summary>Classify an item as File/Folder/App from its path and optional hints.</summary>
    public static SearchItemKind ClassifyKind(string path, bool? isFolderHint = null, string? itemType = null)
    {
        if (isFolderHint == true)
            return SearchItemKind.Folder;
        if (string.Equals(itemType, "Directory", StringComparison.OrdinalIgnoreCase))
            return SearchItemKind.Folder;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            // No extension and not flagged a folder: when we were told it is a file, keep it a file.
            return isFolderHint == false ? SearchItemKind.File : SearchItemKind.Folder;
        }

        return AppExtensions.Contains(ext) ? SearchItemKind.App : SearchItemKind.File;
    }

    /// <summary>Score the display name against the query (prefix/exact &gt; word-start &gt; substring).</summary>
    public static double ScoreName(string name, string query)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(query))
            return 0;

        var noExt = Path.GetFileNameWithoutExtension(name);

        if (name.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            noExt.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            noExt.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 70;

        var idx = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx > 0 && !char.IsLetterOrDigit(name[idx - 1]))
            return 55; // match starts at a word boundary (after space, '-', '_', '.')
        if (idx >= 0)
            return 40; // plain substring match

        return 20; // matched on something other than the display name (rare)
    }

    /// <summary>Recency boost: newer files bubble up. Cheap bucketed curve.</summary>
    public static double RecencyBoost(DateTimeOffset? modified, DateTimeOffset nowUtc)
    {
        if (modified is not { } m)
            return 0;

        var ageDays = (nowUtc - m.ToUniversalTime()).TotalDays;
        if (ageDays < 0) ageDays = 0; // clock skew guard

        return ageDays switch
        {
            < 1 => 20,
            < 7 => 12,
            < 30 => 7,
            < 180 => 3,
            < 365 => 1,
            _ => 0,
        };
    }

    /// <summary>Score every candidate, order best-first, and cap at <paramref name="max"/>.</summary>
    public static List<SearchResult> Finalize(
        IEnumerable<(string name, string path, SearchItemKind kind, DateTimeOffset? modified)> items,
        string query,
        int max)
    {
        var now = DateTimeOffset.UtcNow;

        return items
            .Select(i => new SearchResult
            {
                Name = i.name,
                Path = i.path,
                Kind = i.kind,
                Modified = i.modified,
                Score = ScoreName(i.name, query) + RecencyBoost(i.modified, now),
            })
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Modified ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }
}

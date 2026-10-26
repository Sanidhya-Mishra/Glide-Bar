using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Search;

/// <summary>
/// The <see cref="ISearchProvider"/> GlideBar consumes.
///
/// If a "complete" provider is available (Everything, which already indexes every drive instantly)
/// it is used on its own. Otherwise the "partial" providers run in parallel and their results are
/// merged — in practice <see cref="WindowsSearchProvider"/> (the indexed system drive) plus
/// <see cref="DriveSearchProvider"/> (every other drive) — then de-duplicated by path and re-ranked.
/// Availability is re-checked per query, so a later-started Everything is picked up without a restart.
/// </summary>
public sealed class AggregateSearchProvider : ISearchProvider
{
    private readonly ISearchProvider? _complete;
    private readonly IReadOnlyList<ISearchProvider> _partials;

    /// <param name="complete">A provider that already covers all drives (e.g. Everything), or null.</param>
    /// <param name="partials">Providers whose results are merged when no complete provider is available.</param>
    public AggregateSearchProvider(ISearchProvider? complete, params ISearchProvider[] partials)
    {
        _complete = complete;
        _partials = partials ?? Array.Empty<ISearchProvider>();
    }

    public string Name
    {
        get
        {
            if (IsReady(_complete)) return _complete!.Name;
            var names = _partials.Where(IsReady).Select(p => p.Name).ToArray();
            return names.Length == 0 ? "GlideBar Search (unavailable)" : string.Join(" + ", names);
        }
    }

    public bool IsAvailable => IsReady(_complete) || _partials.Any(IsReady);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, int max, CancellationToken ct = default)
    {
        if (max <= 0) max = 30;

        // A complete provider already spans every drive — use it alone (no redundant disk walk).
        if (IsReady(_complete))
            return await SafeSearch(_complete!, query, max, ct).ConfigureAwait(false);

        var active = _partials.Where(IsReady).ToList();
        if (active.Count == 0)
            return Array.Empty<SearchResult>();

        if (active.Count == 1)
            return await SafeSearch(active[0], query, max, ct).ConfigureAwait(false);

        // Run the partial providers concurrently and merge.
        var lists = await Task.WhenAll(active.Select(p => SafeSearch(p, query, max, ct))).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return Array.Empty<SearchResult>();

        // De-duplicate by path, keeping the best-scored hit for each.
        var byPath = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in lists)
            foreach (var r in list)
            {
                if (string.IsNullOrEmpty(r.Path)) continue;
                if (!byPath.TryGetValue(r.Path, out var existing) || r.Score > existing.Score)
                    byPath[r.Path] = r;
            }

        return byPath.Values
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Modified ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static bool IsReady(ISearchProvider? p)
    {
        try { return p is not null && p.IsAvailable; }
        catch { return false; }
    }

    private static async Task<IReadOnlyList<SearchResult>> SafeSearch(
        ISearchProvider p, string query, int max, CancellationToken ct)
    {
        try { return await p.SearchAsync(query, max, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Array.Empty<SearchResult>(); }
        catch { return Array.Empty<SearchResult>(); }
    }
}

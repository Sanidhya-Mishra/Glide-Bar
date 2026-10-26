using System.Collections.Concurrent;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Search;

/// <summary>
/// Searches file/folder NAMES across the ready drives the Windows Search index does NOT reach (the
/// system drive is left to the index). It walks the filesystem with an explicit stack so the time
/// budget and cancellation are checked on EVERY entry — not just when a match is found — which keeps
/// even a sparse query (few matches) strictly bounded. Reparse points (junctions/symlinks) and the
/// recycle bin / volume-info folders are skipped. Together with <see cref="WindowsSearchProvider"/>
/// (the indexed system drive) this gives "search across all drives".
/// </summary>
public sealed class DriveSearchProvider : ISearchProvider
{
    private const int HardCap = 120;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(1500);

    // Build / cache / dependency folders are huge and never what a user searches for by name.
    // Not descending into them lets the time budget reach real content far deeper.
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", "__pycache__", ".vs", ".vscode", ".idea",
        "obj", "bin", "intermediate", "deriveddatacache", "binaries", "site-packages",
        "packages", ".nuget", ".gradle", ".cache", ".conda", "venv", ".venv", "env",
        "crashes", "crashreportclient", "$recycle.bin", "system volume information",
    };

    // Dirs whose name starts with one of these are also skipped (they carry volatile suffixes).
    private static readonly string[] SkipDirPrefixes = { "webcache", "gpucache", "codecache", "shadercache" };

    private static bool SkipDir(string name)
    {
        if (SkipDirs.Contains(name)) return true;
        foreach (var p in SkipDirPrefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public string Name => "All drives";

    public bool IsAvailable => GetRoots().Count > 0;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2) return Array.Empty<SearchResult>();

        if (max <= 0) max = 30;
        max = Math.Min(max, HardCap);
        var poolCap = max * 8;

        var roots = GetRoots();
        if (roots.Count == 0) return Array.Empty<SearchResult>();

        using var timeout = new CancellationTokenSource(Budget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;

        var bag = new ConcurrentBag<(string, string, SearchItemKind, DateTimeOffset?)>();
        var found = new int[1];

        try
        {
            await Task.WhenAll(roots.Select(root =>
                Task.Run(() => WalkRoot(root, term, bag, found, poolCap, token), token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* budget hit or caller cancelled — use what we gathered */ }
        catch { /* defensive: never surface a walk failure */ }

        // If the user already typed the next character, drop this now-stale result set.
        if (ct.IsCancellationRequested) return Array.Empty<SearchResult>();

        return SearchRanking.Finalize(bag, term, max);
    }

    private static void WalkRoot(
        string root, string term,
        ConcurrentBag<(string, string, SearchItemKind, DateTimeOffset?)> bag,
        int[] found, int poolCap, CancellationToken token)
    {
        // Breadth-first: cover every folder level-by-level so files in sub-folders across the whole
        // drive surface before the walk descends into any one deep (often cache-heavy) branch.
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            if (token.IsCancellationRequested || Volatile.Read(ref found[0]) >= poolCap) return;

            var dir = queue.Dequeue();
            IEnumerator<FileSystemInfo> en;
            try { en = new DirectoryInfo(dir).EnumerateFileSystemInfos().GetEnumerator(); }
            catch { continue; } // gone / denied

            using (en)
            {
                while (true)
                {
                    // Budget + cancellation are checked on every single entry, so a sparse query
                    // (few matches) can't run away walking a whole drive.
                    if (token.IsCancellationRequested || Volatile.Read(ref found[0]) >= poolCap) return;

                    FileSystemInfo info;
                    try { if (!en.MoveNext()) break; info = en.Current; }
                    catch { break; } // enumeration error mid-directory — abandon it

                    FileAttributes attr;
                    try { attr = info.Attributes; } catch { continue; }

                    if ((attr & FileAttributes.ReparsePoint) != 0) continue; // no junction/symlink loops

                    var isDir = (attr & FileAttributes.Directory) != 0;
                    var full = info.FullName;

                    if (info.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        var kind = isDir
                            ? SearchItemKind.Folder
                            : SearchRanking.ClassifyKind(full, isFolderHint: false, itemType: null);
                        DateTimeOffset? mod = null;
                        try { mod = new DateTimeOffset(info.LastWriteTime); } catch { /* skip */ }
                        bag.Add((info.Name, full, kind, mod));
                        Interlocked.Increment(ref found[0]);
                    }

                    // Recurse into real folders only — skip build/cache/dependency trees (above)
                    // so the time budget reaches genuine user content far deeper.
                    if (isDir && !SkipDir(info.Name))
                        queue.Enqueue(full);
                }
            }
        }
    }

    /// <summary>Ready fixed/removable drives, excluding the system drive (covered by the index).</summary>
    private static List<string> GetRoots()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory); // e.g. "C:\"
        var list = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                var root = d.RootDirectory.FullName;
                if (systemRoot is not null && string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase))
                    continue; // system drive → Windows Search
                list.Add(root);
            }
            catch { /* drive query failed; skip */ }
        }
        return list;
    }

}

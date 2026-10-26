using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.Search;

/// <summary>
/// Optional, best-effort <see cref="ISearchProvider"/> backed by voidtools "Everything" via its
/// native IPC SDK (<c>Everything64.dll</c> / <c>Everything32.dll</c>). When Everything is running
/// and the SDK DLL is discoverable, queries are near-instant. GlideBar does not ship the SDK DLL,
/// so on most machines this provider simply reports <see cref="IsAvailable"/> = <c>false</c> and the
/// aggregator falls back to Windows Search. It never throws into the caller and never blocks the build.
/// </summary>
public sealed class EverythingSearchProvider : ISearchProvider
{
    private const int HardCap = 60;

    // Serializes access to the DLL's process-wide global search state.
    private static readonly object QueryGate = new();

    private readonly Lazy<bool> _available;

    public EverythingSearchProvider()
        => _available = new Lazy<bool>(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Name => "Everything";

    /// <summary>True only when Everything is running AND its SDK DLL could be loaded and answered.</summary>
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
            return await Task.Run(() => RunQuery(trimmed, max, ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<SearchResult>();
        }
        catch
        {
            return Array.Empty<SearchResult>();
        }
    }

    private static List<SearchResult> RunQuery(string trimmed, int max, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (QueryGate)
        {
            ct.ThrowIfCancellationRequested();

            Native.Everything_SetMatchPath(false);      // match on file name, like Windows Search
            Native.Everything_SetRegex(false);
            Native.Everything_SetRequestFlags(
                Native.REQUEST_FULL_PATH_AND_FILE_NAME | Native.REQUEST_DATE_MODIFIED);
            Native.Everything_SetMax((uint)Math.Clamp(max * 2, max, HardCap));
            Native.Everything_SetSearchW(trimmed);

            if (!Native.Everything_QueryW(true))
                return new List<SearchResult>();

            ct.ThrowIfCancellationRequested();

            var count = Native.Everything_GetNumResults();
            var rows = new List<(string, string, SearchItemKind, DateTimeOffset?)>((int)count);
            var buffer = new StringBuilder(32768);

            for (uint i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();

                buffer.Clear();
                Native.Everything_GetResultFullPathNameW(i, buffer, (uint)buffer.Capacity);
                var path = buffer.ToString();
                if (string.IsNullOrEmpty(path))
                    continue;

                var isFolder = Native.Everything_IsFolderResult(i);
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;

                DateTimeOffset? modified = null;
                if (Native.Everything_GetResultDateModified(i, out var ft) && ft > 0)
                {
                    try { modified = DateTimeOffset.FromFileTime(ft); } catch { /* skip */ }
                }

                var kind = SearchRanking.ClassifyKind(path, isFolderHint: isFolder);
                rows.Add((name, path, kind, modified));
            }

            try { Native.Everything_CleanUp(); } catch { /* best effort */ }

            return SearchRanking.Finalize(rows, trimmed, max);
        }
    }

    private static bool Detect()
    {
        try
        {
            if (!IsEverythingRunning())
                return false;

            // Probe the SDK DLL: a non-zero major version means the DLL loaded and responded.
            return Native.Everything_GetMajorVersion() > 0;
        }
        catch
        {
            // DllNotFoundException / EntryPointNotFoundException => SDK not shipped/installed.
            return false;
        }
    }

    private static readonly string[] IpcWindowClasses =
    {
        "EVERYTHING_TASKBAR_NOTIFICATION",
        "EVERYTHING",
    };

    private static bool IsEverythingRunning()
    {
        foreach (var cls in IpcWindowClasses)
        {
            if (Native.FindWindowW(cls, null) != IntPtr.Zero)
                return true;
        }

        try
        {
            if (Process.GetProcessesByName("Everything").Length > 0)
                return true;
        }
        catch { /* ignore */ }

        return false;
    }

    /// <summary>
    /// P/Invoke surface for the Everything SDK plus a resolver that also looks in a running
    /// Everything's install directory (the DLL is not bundled with GlideBar).
    /// </summary>
    private static class Native
    {
        private const string Dll = "Everything64.dll";

        public const uint REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004;
        public const uint REQUEST_DATE_MODIFIED = 0x00000040;

        static Native()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
            }
            catch { /* resolver already set or unsupported; DllImport will use default search */ }
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!libraryName.StartsWith("Everything", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            var names = Environment.Is64BitProcess
                ? new[] { "Everything64.dll", "Everything.dll" }
                : new[] { "Everything32.dll", "Everything.dll" };

            foreach (var dir in CandidateDirectories())
            {
                foreach (var n in names)
                {
                    try
                    {
                        var full = Path.Combine(dir, n);
                        if (File.Exists(full) && NativeLibrary.TryLoad(full, out var handle))
                            return handle;
                    }
                    catch { /* try next */ }
                }
            }

            // Fall back to the OS default search path (PATH, app dir).
            foreach (var n in names)
            {
                if (NativeLibrary.TryLoad(n, out var handle))
                    return handle;
            }

            return IntPtr.Zero;
        }

        private static IEnumerable<string> CandidateDirectories()
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
                yield return baseDir;

            // Where the running Everything.exe lives, if any.
            Process[] procs = Array.Empty<Process>();
            try { procs = Process.GetProcessesByName("Everything"); } catch { }
            foreach (var p in procs)
            {
                string? dir = null;
                try { dir = Path.GetDirectoryName(p.MainModule?.FileName); } catch { }
                if (!string.IsNullOrEmpty(dir))
                    yield return dir;
            }

            foreach (var pf in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     })
            {
                if (!string.IsNullOrEmpty(pf))
                    yield return Path.Combine(pf, "Everything");
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

        [DllImport(Dll, CharSet = CharSet.Unicode)]
        public static extern uint Everything_SetSearchW(string lpSearchString);

        [DllImport(Dll)]
        public static extern void Everything_SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool bEnable);

        [DllImport(Dll)]
        public static extern void Everything_SetRegex([MarshalAs(UnmanagedType.Bool)] bool bEnable);

        [DllImport(Dll)]
        public static extern void Everything_SetRequestFlags(uint dwRequestFlags);

        [DllImport(Dll)]
        public static extern void Everything_SetMax(uint dwMax);

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool bWait);

        [DllImport(Dll)]
        public static extern uint Everything_GetNumResults();

        [DllImport(Dll, CharSet = CharSet.Unicode)]
        public static extern void Everything_GetResultFullPathNameW(uint nIndex, StringBuilder lpString, uint nMaxCount);

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Everything_IsFolderResult(uint nIndex);

        [DllImport(Dll)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Everything_GetResultDateModified(uint nIndex, out long lpFileTime);

        [DllImport(Dll)]
        public static extern uint Everything_GetMajorVersion();

        [DllImport(Dll)]
        public static extern void Everything_CleanUp();
    }
}

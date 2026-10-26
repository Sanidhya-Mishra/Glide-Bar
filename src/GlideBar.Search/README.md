# GlideBar.Search

File search + launcher service for GlideBar. Implements the `GlideBar.Core` contracts
`ISearchProvider` and `ILauncher`. Target framework: `net10.0-windows`.

## What's in here

| File | Type | Role |
|------|------|------|
| `WindowsSearchProvider.cs` | `ISearchProvider` | **Default.** Queries the Windows Search index via OLE DB. |
| `EverythingSearchProvider.cs` | `ISearchProvider` | Optional, best-effort voidtools *Everything* via its native SDK IPC. |
| `AggregateSearchProvider.cs` | `ISearchProvider` | The provider GlideBar consumes: picks the best available (Everything > Windows Search). |
| `ShellLauncher.cs` | `ILauncher` | Opens / reveals / launches items through the Windows shell. |
| `SearchRanking.cs` | internal | Shared scoring + ordering used by both providers. |
| `ServiceCollectionExtensions.cs` | — | `AddGlideBarSearch(this IServiceCollection)` DI registration. |

## How search works

### Windows Search (default)

Reuses the OS indexer — GlideBar keeps **no** always-on index of its own. It connects to the
Windows Search OLE DB provider:

```
provider=Search.CollatorDSO;Extended Properties='Application=Windows'
```

and runs (schematically, for `max = 30`):

```sql
SELECT TOP 60 System.ItemNameDisplay, System.ItemPathDisplay,
              System.DateModified, System.ItemType
FROM   SystemIndex
WHERE  System.ItemNameDisplay LIKE '%<query>%' ESCAPE '\' AND SCOPE='file:'
ORDER  BY System.DateModified DESC
```

- `SCOPE='file:'` restricts hits to file-system items (no mail/other stores).
- `ORDER BY System.DateModified DESC` + `TOP (max*2, capped at 60)` pulls a recent candidate pool,
  which is then re-ranked in code and capped to `max`.
- The blocking OLE DB calls run on a thread-pool thread (`Task.Run`), and the `CancellationToken`
  both aborts the read loop and calls `OleDbCommand.Cancel()` on the in-flight provider call.

**Query escaping (injection / quoting safety).** The `Search.CollatorDSO` provider does **not**
support OLE DB command parameters, so the query term is escaped by hand before it goes into the
single-quoted SQL literal:

- `'` → `''` (SQL string-literal escaping — prevents breaking out of the literal / injection),
- `%`, `_`, `[`, `\` → backslash-escaped and the clause emits `ESCAPE '\'` so the user's text is
  matched **literally** instead of as LIKE wildcards.

Verified live on-machine: `o'brien` and `50% off` both return gracefully (no exception, no
malformed SQL).

**Availability.** `IsAvailable` runs a one-time cached probe (`SELECT TOP 1 ... FROM SystemIndex`).
If the OLE DB provider isn't registered or the indexer is off, it returns `false` and the aggregator
falls back; searches then simply return an empty list rather than throwing.

### Everything (optional, best-effort)

`EverythingSearchProvider` is a thin wrapper over the voidtools *Everything* native SDK
(`Everything64.dll` / `Everything32.dll`, P/Invoke).

**Detection** (`IsAvailable`, cached):

1. Is Everything running? — `FindWindow` on the IPC window classes
   (`EVERYTHING_TASKBAR_NOTIFICATION`, `EVERYTHING`) or a process named `Everything`.
2. Is the SDK DLL usable? — `Everything_GetMajorVersion() > 0`. A custom `DllImportResolver`
   tries the app directory, the running Everything's install directory, and the standard
   `Program Files\Everything` locations before the default OS search path.

GlideBar **does not ship** the Everything SDK DLL. If it isn't discoverable (the common case),
`IsAvailable` is `false` and the provider is skipped — it never blocks the build or throws.

When available, it sets request flags (full path + date modified), queries by file name
(`Everything_SetMatchPath(false)`), and feeds results through the same ranker. The DLL's global
query state is serialized behind a lock.

### Ranking (`SearchRanking`)

Both providers score identically so results feel consistent:

- **Name score:** exact / name-without-extension match (100) > prefix (70) > word-boundary
  substring (55) > plain substring (40).
- **Recency boost:** newer `DateModified` adds up to +20 (bucketed: <1d, <7d, <30d, <180d, <365d).
- Ties break on recency, then name. Final list is capped to `max`.
- **Kind mapping:** `System.ItemType == "Directory"` (or Everything's folder flag) → `Folder`;
  `.exe/.lnk/.appref-ms/.msi/.url/.bat/.cmd` → `App`; otherwise `File`.

## Launcher behaviors (`ShellLauncher`)

All methods are wrapped in try/catch — a bad path or missing target **never** throws into the UI.

| Method | Behavior |
|--------|----------|
| `OpenPath(path)` | `Process.Start(UseShellExecute=true)`. A `shell:` moniker is launched via `explorer.exe`. |
| `Open(SearchResult)` | Opens the result's path. |
| `Open(PinnedItem)` | For `PinnedKind.App` whose path looks like an AUMID (UWP/Store), launches `shell:AppsFolder\<AUMID>` via `explorer.exe`; otherwise shell-opens the path with the pin's optional `Arguments`. |
| `RevealInExplorer(path)` | `explorer.exe /select,"path"` — highlights the item in its folder. |
| `OpenContainingFolder(path)` | Opens the directory (or the item's parent directory). |

AUMID heuristic: not an existing file/folder path, and either contains `!`
(e.g. `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`) or is a bare token with no path separators.

## DI registration

```csharp
using GlideBar.Search;

services.AddGlideBarSearch();
```

Registers (via `TryAddSingleton`, so a host's own registration wins):

- `ISearchProvider` → `AggregateSearchProvider(EverythingSearchProvider, WindowsSearchProvider)`
- `ILauncher` → `ShellLauncher`

## Packages

- `System.Data.OleDb` — Windows Search OLE DB access.
- `Microsoft.Extensions.DependencyInjection.Abstractions` — the `AddGlideBarSearch` extension.

## Build

```
dotnet build "GlideBar.Search\GlideBar.Search.csproj" -c Debug
```

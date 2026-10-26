# GlideBar.Storage

Persistence for GlideBar: settings/pins JSON storage, the settings service, and the
"start with Windows" autostart toggle. Targets `net10.0-windows`, depends only on
`GlideBar.Core` and the DI abstractions.

Implements three `GlideBar.Core.Abstractions` contracts:

| Contract | Implementation |
|----------|----------------|
| `IStorage` | `JsonStorage` |
| `ISettingsService` | `SettingsService` |
| `IAutostartService` | `AutostartService` |

## Registration (DI)

```csharp
using GlideBar.Storage;

services.AddGlideBarStorage(); // IStorage, ISettingsService, IAutostartService as singletons
```

## File location & format

- **Location:** `%AppData%\GlideBar\config.json` (`GlideBar.Core.AppPaths.ConfigFile`).
  `JsonStorage` ensures `AppPaths.DataDirectory` exists before writing.
- **Format:** indented JSON via `System.Text.Json`. Enums are written as strings
  (`"Left"`, `"App"`). A source-generated `JsonSerializerContext` (`StorageJsonContext`)
  covers `AppSettings` (and nested `PinnedItem` / `BarPlacement` / enums), keeping that path
  reflection-free and trimming/AOT-friendly. A reflection resolver is chained as a fallback so
  the generic `IStorage` contract still serves any other type; drop that fallback if full
  trimming is later enabled.
- **Keys:** `IStorage` keys map to `%AppData%\GlideBar\<key>.json`. The key is reduced to its
  file name (path-traversal safe) and gets a `.json` extension. The settings service uses the
  `"config"` key.
- **Reads are tolerant:** a missing, corrupt, or locked file returns `null` (never throws),
  so a damaged config degrades to defaults instead of crashing.

## Atomic writes

Every save serializes to a temp file (`.<name>.<guid>.tmp`) in the **same directory** (same
volume), flushes it, then swaps it over the target with `File.Replace(..., ignoreMetadataErrors:
true)` — or `File.Move` when no target exists yet. The target is therefore never observed
half-written, so a crash mid-write cannot corrupt the config. Temp files are cleaned up on
failure.

## Debounced saves

`SettingsService.SaveAsync` is **debounced (~500 ms)**: each call cancels the previous pending
window and starts a new one, so a burst of rapid changes coalesces into a single disk write.
A linked `CancellationTokenSource` drives the debounce; a `SemaphoreSlim` serializes the actual
writes; a lock guards all settings mutation and a deep-ish snapshot is taken under that lock
before serialization, so writes never race a concurrent mutation. The whole service is
thread-safe.

`AddPin` / `RemovePin` / `MovePin` each maintain `PinnedItem.Order` as a compact `0..n-1`
sequence, raise `PinsChanged`, and trigger a debounced save. `LoadAsync` raises both
`SettingsChanged` and `PinsChanged`.

## Schema migration

`AppSettings.SchemaVersion` (current: **1**). `SettingsService.Migrate` is a `switch` on the
loaded version — v1 is a no-op stub today. Future migrations chain upward (add a `case`), and an
unknown/garbage version is stamped to the current version. Migration runs on load, before the
settings become live.

## Autostart mechanism

`AutostartService` toggles the per-user Run key
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `GlideBar`
(`Microsoft.Win32.Registry`, no package needed on `net10.0-windows`, **no elevation required**).

- `IsEnabled` reflects the actual registry state (value present and non-empty).
- `SetEnabled(true)` writes the current executable path (`Environment.ProcessPath`, with a
  `Process.MainModule` fallback), quoted to survive spaces.
- `SetEnabled(false)` removes the value (no-op if absent).

## Verification

A throwaway harness confirmed on this machine (26/26 checks): `AppSettings` round-trips through
`JsonStorage` (scalars, bools, enums, placement, pins), atomic overwrite with no `.tmp`
leftovers, corrupt/missing-file tolerance, debounced save + pin CRUD ordering, and the autostart
registry toggle reading back correctly (value written/removed); machine state was restored
afterward.

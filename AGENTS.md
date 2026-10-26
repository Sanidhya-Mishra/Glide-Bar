# Parallel work distribution

The build is split into independent workstreams so agents can run in parallel **without editing the same
files**. The rule that makes this safe: each service is its own .NET project and depends only on the
**`GlideBar.Core`** contracts. Core is defined *first*; then the service agents run concurrently.

## Dependency order

```
            ┌──────────────────────────────┐
            │  (0) Core contracts + solution │   ← must exist before parallel work
            └──────────────┬───────────────┘
                           │
      ┌────────────────────┼─────────────────────┐
      ▼                    ▼                     ▼
(A) HardwareMonitor   (B) Search+Launcher   (C) Storage+Settings     ← run in parallel
      └────────────────────┴─────────────────────┘
                           │
                           ▼
            (D) App shell: window/hover/tray/hotkey/drag            ← after Core; UI chrome
                           │
                           ▼
            (E) UI theme: implement the CHOSEN design               ← after a mockup is picked
```

## Workstreams

### (0) Core & scaffolding — *orchestrator / first*
Solution + projects, DI container, shared models & interfaces, logging, settings schema. Blocks everything else.
**Owns:** `GlideBar.sln`, `GlideBar.Core/`.

### (A) HardwareMonitor agent
Implement `IMetricsProvider`: cheap perf-counter/Win32 path (CPU %, RAM, GPU %) + LibreHardwareMonitor temps
(opt-in, elevation-aware). Pause-when-hidden hook. Smoke test on this machine.
**Owns:** `GlideBar.HardwareMonitor/`.

### (B) Search & Launcher agent
Implement `ISearchProvider` (Windows Search default, Everything if present) + `ILauncher` (open/reveal/launch
pinned app). Debounced query, result ranking, cancellation.
**Owns:** `GlideBar.Search/`.

### (C) Storage & Settings agent
Implement `IStorage` + `ISettingsService`: JSON in `%AppData%\GlideBar`, atomic/debounced writes, pinned-items
CRUD, schema versioning, migration stub.
**Owns:** `GlideBar.Storage/`.

### (D) App shell agent
WPF overlay: transparent topmost trigger window, hover dwell/grace reveal, drag + edge/corner snap,
multi-monitor, tray icon (H.NotifyIcon), global hotkey, opt-in autostart. Skeleton (unstyled) views that bind
to injected services.
**Owns:** `GlideBar.App/` (shell, window management, infra) — **not** the final visual theme.

### (E) UI theme agent — *after a design is picked*
Implement the chosen mockup as WPF styles/resources: acrylic surface, dark/light tokens, metric widgets
(gauges/bars), search box, pinned list, transitions. Consumes (D)'s view structure.
**Owns:** `GlideBar.App/Themes/`, control templates, styles.

## Conflict-avoidance rules
- One project = one owner at a time. Cross-project changes go through the Core interfaces, not direct edits.
- No agent edits `GlideBar.Core` after M1 without coordinating (it's the shared contract).
- `GlideBar.App` shell (D) and theme (E) are sequenced, not parallel, since they share the project.

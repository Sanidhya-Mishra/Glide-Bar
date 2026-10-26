# GlideBar.HardwareMonitor

Real `IMetricsProvider` for GlideBar. Layered **cheap-first** so the always-on path costs almost nothing.

## Tiers
- **Tier A — always on, no admin:**
  - **CPU load %** — Win32 `GetSystemTimes` deltas.
  - **RAM** — `GlobalMemoryStatusEx` (used/total bytes + percent).
  - **GPU load % + dedicated VRAM** — `GPU Engine` / `GPU Adapter Memory` performance counters (Win10 1709+).
- **Tier B — opt-in temperatures** (`TryEnableTemperaturesAsync`): `LibreHardwareMonitorLib`.
  - GPU temperature usually reads in user mode.
  - **CPU-core temperature generally requires running elevated** (kernel driver). When not elevated it's reported as unavailable.
  - `Capabilities` reflects what is actually readable on this machine.

## Lifecycle / performance
- Sampling runs on a `System.Threading.Timer`. `Start()` begins, `SetInterval()` changes cadence (default 1 s).
- **`Stop()` fully halts sampling and releases the timer AND the LibreHardwareMonitor handle** (and its ring-0
  driver if loaded) — the UI calls `Stop()` whenever the bar collapses, so idle cost is ~0.
- Reader/counter handles are reused across ticks; a bad tick is swallowed and never tears down the timer.

## DI
```csharp
services.AddGlideBarHardwareMonitor(); // registers IMetricsProvider (singleton)
```

## Notes
- The default always-on path never loads the sensor driver — temperatures are strictly opt-in.
- Integrated Intel GPUs often expose no temperature telemetry (hardware limitation); AMD VRAM usage may be unavailable.

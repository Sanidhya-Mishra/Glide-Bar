# Design spec — Adaptive Morph Bar (chosen design)

The approved look is a **hybrid** of concept **01 Top Notch Pill** and **02 Right/Left Edge Dock**: one bar that
changes orientation based on where it lives.

## Behavior

**Default:** a horizontal rounded **pill**, floating near the top-center of the primary monitor.

**Movable:** fully draggable anywhere, on any monitor (drag the whole bar, or a dedicated grip in the collapsed state).

**Auto-transform (the key feature):**
| Where it sits | Orientation |
|---------------|-------------|
| Floating (not touching an edge) | Horizontal pill |
| Snapped to the **top** or **bottom** edge | Horizontal pill |
| Snapped/dragged to the **left** or **right** edge | **Vertical dock** |

- **Magnetic snap:** within ~24 px of an edge the bar snaps and (for left/right) morphs to vertical.
- **Smooth morph:** animate the orientation + size change (layout transition), honoring `prefers-reduced-motion` /
  the "reduce effects" setting (then it just swaps instantly).
- Placement (monitor + edge/floating + offset + orientation) is persisted via `ISettingsService`.

**Hover reveal:**
- At rest the bar **collapses** to a small handle/sliver at its anchored edge.
- Mouse approach/hover → **expand** after a short dwell (~120 ms).
- Mouse leaves → **collapse** after a grace period (~400 ms). While hovered or pinned-open it stays.
- Global hotkey (default `Ctrl`+`Space`) force-summons/dismisses it.

## Contents (reflow per orientation)

| Element | Horizontal | Vertical |
|---------|-----------|----------|
| **Search** | inline field with placeholder | icon that expands to a field/overlay |
| **Pins** | horizontal row of tiles | vertical column of tiles |
| **Vitals** | ring-gauge cluster (CPU/RAM/GPU/Temp) | stacked ring gauges |
| **Overflow / settings** | trailing `⋯` / gear | bottom `⋯` / gear |

Divider lines separate the three zones (Search · Pins · Vitals), as in concept 11.

## Visual language
- Minimal, **acrylic/translucent** surface with blur; soft rounding (pill = fully rounded; dock = 15 px radius).
- **Dark + light** themes (follow system, with manual override).
- Accent: indigo `#5b7cfa` (with violet `#8a5cf6` secondary). Semantic only for load/temperature:
  good `#1fb866`, warn `#f0a020`, critical `#ef4b4b`.
- Type: Segoe UI Variable / Inter, tabular numerals for all metric values.
- Elevated drop shadow; shadow grows slightly while dragging.

## Interaction constants (tunable in settings)
- Dwell-in 120 ms · grace-out 400 ms · drag threshold 4 px · edge snap distance 24 px · morph animation 180 ms.

## States to implement
`Collapsed (handle)` · `Expanded-Horizontal` · `Expanded-Vertical` · `Dragging` · `Search-active` · `Settings-open`.

## Performance rules (hard requirements)
- No animation/timer loops while collapsed. Metric polling runs **only while expanded/visible** (`IMetricsProvider.Stop()` on collapse).
- No per-frame topmost re-assertion; set topmost once and on monitor/display changes only.
- Acrylic blur is optional (off in "reduce effects"); never repaint the blur continuously.

This spec drives the **GlideBar.App** workstream (see [`../AGENTS.md`](../AGENTS.md)). Reference mockups 01, 02, 11
in the design gallery.

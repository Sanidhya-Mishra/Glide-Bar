# Changelog

All notable changes to GlideBar are documented here. This project follows
[Semantic Versioning](https://semver.org/) (MAJOR.MINOR.PATCH).

## [1.1.0] — 2026-10-26

### Added
- **Real app & file icons on pinned tiles.** Pinned items now show the target's actual
  Windows icon (e.g. Chrome's icon for Chrome) instead of a generic kind glyph, so pins are
  identifiable at a glance without hovering. The icon is extracted off the UI thread; the kind
  glyph (app/folder/document) remains as a fallback while it loads or if none is available.
  The same applies to the "+" add-panel tiles.

### Fixed
- **Shortcut (`.lnk`) icons looked poor.** Start-menu shortcuts rendered a low-resolution icon
  with the shell's shortcut-arrow overlay baked in. Icons are now pulled from the extra-large
  (48px) system image list by index, which is crisper and returns the *base* icon — no more
  shortcut-arrow badge.

## [1.0.0] — 2026-10-26

### Added
- Initial release. Hover-activated overlay bar for Windows 11:
  - Live CPU / RAM / GPU load and CPU/GPU temperature gauges.
  - Collapsible file / folder / app search (open or reveal results).
  - Pinned apps, files and folders for one-click launch; drag-and-drop to pin.
  - Hover-to-reveal, drag to any screen edge/corner, multi-monitor aware; vertical and
    horizontal docking.
  - Tray icon (Show/Hide · Pause · Settings · Start with Windows · Quit) and a global hotkey
    (Ctrl+Space).
  - Dark / light / system themes; settings, pins, position and theme persist to
    `%AppData%\GlideBar`.
  - One-click per-user installer (no admin) plus a portable self-contained executable.

[1.1.0]: https://github.com/Sanidhya-Mishra/Glide-Bar/releases/tag/v1.1.0
[1.0.0]: https://github.com/Sanidhya-Mishra/Glide-Bar/releases/tag/v1.0.0

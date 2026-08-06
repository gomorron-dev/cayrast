# Changelog

All notable changes to Cayrast are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Because Cayrast is pre-1.0, the SDK's public surface may change between minor
versions. Such changes are always called out under **Changed** with a migration note.

---

## [Unreleased]

### Added

Phase 1 — the application now launches, registers its hotkey, and shows a window.

- Warm launcher window: a hidden WPF host with a WebView2 created once at startup, so
  Alt+Space is a show call rather than a browser initialisation
- WebView2 frontend served from `https://shell.cayrast.local/` via virtual host
  mapping, with a restrictive Content-Security-Policy
- Global hotkey (default Alt+Space), tray icon that survives an Explorer restart, and
  single-instance handling where a second launch activates the first
- Settings: typed JSON with atomic writes, debounced saves, schema migration,
  normalisation, and recovery from a corrupt file by quarantining it
- Multi-monitor positioning in device pixels, correct under mixed DPI
- Svelte 5 + Vite frontend with a CSS-variable theme system, light/dark/high-contrast
  support, and reduced-motion handling
- `ChildProcessJob`, which uses a Windows job object so WebView2 children cannot
  outlive the shell even when it is killed

### Fixed

- Activation from a second launch never worked: it used a broadcast window message,
  which Windows does not deliver to message-only windows. Replaced with a named event.
- The launcher appeared and vanished in the same instant, because Windows reports a
  spurious deactivation while the foreground is still being handed over and
  `HideOnFocusLoss` acted on it.
- Settings containing an explicit `null`, or written by a version with fewer fields,
  could yield null values in non-nullable properties. All settings are now normalised
  after load and after update, which also clamps out-of-range hand-edited values.
- `Alt+Space`-style parsing accepted malformed input such as `"+Space"`, producing a
  binding that did not round-trip through settings.

### Earlier — foundation

- Solution structure: `Abstractions`, `Sdk`, `Core`, `Platform.Windows`,
  `ModuleHost`, `Shell`, and a test project, under central package management
- Core contracts in `Cayrast.Abstractions`:
  - `ISearchProvider` with streaming results and mandatory cancellation
  - `CommandDescriptor` / `ICommandHandler`, with descriptors driving `help`
  - `ModuleManifest`, `ICayrastModule`, `IModuleContext`, `ModulePermission`,
    `ModuleTrustLevel`
  - `SettingDescriptor`, so settings are data and therefore searchable
  - `IpcEnvelope`, the single wire format shared by in-process and sandboxed modules
- `ModuleId`, a validated identifier type that prevents malformed manifest ids from
  reaching filesystem paths, database keys, or WebView2 origins
- `CayrastPaths`, resolving all roaming and local storage locations
- `CayrastBrand`, centralising every product name, extension, and virtual host
- Build policy: warnings as errors, documented analyzer suppressions, `.editorconfig`
- 31 tests covering `ModuleId` validation, including path traversal and null-byte cases
- Documentation: README, architecture, roadmap, contributing, security, code of conduct
- CI: build and test on Windows; release workflow triggered by version tags

[Unreleased]: https://github.com/cayrast/cayrast/commits/main

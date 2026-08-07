# Changelog

All notable changes to Cayrast are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Because Cayrast is pre-1.0, the SDK's public surface may change between minor
versions. Such changes are always called out under **Changed** with a migration note.

---

## [0.1.0] - 2026-08-07

### Added

- **File Search**: Bounded breadth-first filesystem search provider (`FileSearchProvider`) searching Desktop, Documents, Downloads, Pictures, and user-configured `IndexedFolders`. Excludes developer directories (`node_modules`, `.git`, `bin`, `obj`, etc.) and system/hidden files.
- **Theme Loader**: `ThemeService` for discovering, deserializing, validating, and applying `.cayrast-theme` packages from disk.
- **Typed Result Activation**: Introduced `ResultTargets.FileTarget` and `ResultTargets.CommandTarget` records to safely handle file activations (`Open`, `RevealInExplorer`, `copy-path`) without verb collisions.
- **CI & Test Hardening**: Strict dependency resolution with `npm ci`, frontend type-checking, and test suite expansion to 180 tests.

Phase 2 — the launcher does its actual job, and the plugin SDK is real.

- **Search**: fzf-style fuzzy matching with highlighted match positions, a streaming
  pipeline that fans out to providers concurrently and paints results as they arrive,
  per-keystroke cancellation, and frecency ranking with exponential decay
- **Application index** via the shell's AppsFolder, covering desktop and Store
  applications through one code path
- **Commands**: `calc`, `uuid`, `base64`, `urlencode`, `md5`/`sha1`/`sha256`/`sha512`,
  `timestamp`, `json`, and `help` — with live preview as you type. The calculator uses
  a hand-written parser rather than a scripting engine, so pasted text can never become
  executed code
- **Settings**: a descriptor registry that generates the settings screen *and* powers
  settings search, so the two cannot drift apart. Searching "glass" finds transparency
- **Modules**: `.cayrast` package loading with manifest validation, a permission
  broker, collectible `AssemblyLoadContext` isolation, and clean unload
- **Theme model** with allow-list validation of CSS values
- **Plugin SDK** plus a worked example module that the test suite packs, installs,
  loads, exercises, and unloads on every run
- **Installer** (Inno Setup) with component selection and a WebView2 runtime check

### Known limitation

- **Module sandboxing is not implemented.** `Cayrast.ModuleHost` is a stub, so modules
  load in-process and their declared permissions are advisory rather than enforced.
  Cayrast reports modules as in-process and logs a warning on every load rather than
  claiming a boundary it does not have. See [SECURITY.md](SECURITY.md).

### Earlier — Phase 1

The application launches, registers its hotkey, and shows a window.

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

[0.1.0]: https://github.com/gomorron-dev/cayrast/releases/tag/v0.1.0

# Roadmap

Cayrast is built in five phases. Each ends at a tree that **builds, runs, and is
demoable** — no phase leaves the repository in a broken intermediate state.

**Current phase: 3 (Official modules) — next.**

---

## Phase 0 — Foundation ✅ complete

- [x] Solution structure, seven projects, central package management
- [x] `Cayrast.Abstractions` contracts: search, commands, modules, permissions, settings, IPC
- [x] Storage path resolution (`CayrastPaths`)
- [x] Warnings-as-errors build, analyzer policy, `.editorconfig`
- [x] Test project with the first suite (`ModuleId` validation, 31 tests)
- [x] Governance docs, issue templates, CI

---

## Phase 1 — Skeleton ✅ complete

The application launches and responds to the hotkey.

- [x] Composition root with `Microsoft.Extensions.DependencyInjection`
- [x] Serilog structured logging to `%LOCALAPPDATA%\Cayrast\Logs`
- [x] Single-instance enforcement, with a second launch activating the first
- [x] Warm window: hidden WPF host created at startup, Acrylic backdrop, rounded corners
- [x] WebView2 on `https://shell.cayrast.local/` via virtual host mapping
- [x] Typed message bridge between the shell UI and Core, over `IpcEnvelope`
- [x] Global hotkey (default **Alt+Space**), rebindable, graceful when already taken
- [x] Tray icon with show/settings/quit, surviving an Explorer restart
- [x] Settings: typed JSON, schema version, migration, atomic save, corruption recovery
- [x] Svelte 5 + Vite frontend with the theme variable system
- [x] Multi-monitor and per-monitor DPI positioning in device pixels
- [x] Job object so WebView2 children never outlive the shell

**Verified:** launches clean with no errors logged, registers Alt+Space, positions
itself on the active monitor, activates on a second launch, hides on focus loss, and
leaves zero orphaned processes after a simulated crash. 85 tests passing.

Three bugs were found and fixed during verification, each recorded in
[ARCHITECTURE.md](ARCHITECTURE.md) because each is a trap worth not re-entering:
broadcast messages never reach a message-only window; a spurious deactivation during
foreground handover hides the window in the frame it appears; and deserialised
settings need explicit normalisation because neither nullability nor property
initialisers survive JSON reliably.

---

## Phase 2 — Engines ✅ complete

The launcher does its actual job, and the SDK is real.

- [x] Streaming search pipeline: concurrent fan-out, per-keystroke cancellation, ranked merge
- [x] fzf-style fuzzy scorer with match-index highlighting
- [x] Frecency store with exponential decay
- [x] Application indexer via the shell's AppsFolder — desktop and Store apps in one path
- [x] Command engine, `help` generated from descriptors, and built-ins
- [x] Settings registry, generated settings screen, settings search by keyword
- [x] Theme model with allow-list value validation
- [x] **Module system**: `.cayrast` packages, manifest validation, permission broker,
      collectible `AssemblyLoadContext` loading, clean unload
- [x] `Cayrast.Sdk` with a worked, tested example module

**Verified:** the example module is packed, installed, loaded, exercised, and unloaded
by the integration suite on every run. Application indexing was checked against
`Get-StartApps` and matches exactly.

### Deferred from Phase 2

Recorded honestly rather than quietly dropped:

- **Filesystem search provider.** The pipeline supports it; the provider is not written.
- **User-defined commands.** The command engine supports registration; there is no UI
  or storage format for user commands yet.
- **`ModuleHost` sandbox process.** `Cayrast.ModuleHost` is a stub. Modules currently
  load in-process, which means the permission broker is advisory rather than
  OS-enforced. The IPC contract that makes the sandbox a configuration change rather
  than a rewrite is in place and used by the WebView2 bridge — but the sandbox itself
  is not built, and until it is, `ModuleTrustLevel.Sandboxed` is aspirational. This is
  the single most important outstanding item.
- **`.cayrast-theme` loading.** The model and validator exist and are tested; the
  loader that reads them from disk and applies them is not written.

---

## Phase 3 — Official modules

Every module below is a separate project consuming the **public** SDK. The rule that
keeps the SDK honest: *if an official module needs an API, that API ships publicly.*

- [ ] Clipboard — history, search, pinning, images, files, encrypted store, OCR
- [ ] File Tools — inspector, hashes, EXIF, hex viewer, strings, duplicate finder
- [ ] Developer Tools *(off by default)* — JSON, encoding, regex, JWT, timestamps, HTTP client
- [ ] QR & Barcode — generate and scan, including screen-region capture
- [ ] Color & Screen Tools — picker, ruler, magnifier, region selector
- [ ] Widgets — clock, calendar, system monitors, notes, todo; detachable
- [ ] Audio Manager — per-application volume, device switching
- [ ] Window Manager — snap layouts, save and restore
- [ ] Automation — multi-step workflows, hotkeys, schedules
- [ ] Spotify *(off by default)* — OAuth PKCE, playback control

---

## Phase 4 — Ship

- [ ] Inno Setup installer: module selection, theme choice, startup option
- [ ] Update system via GitHub Releases — notify, never force
- [ ] Backup and restore (`.cayrast-backup`), config export (`.cayrast-config`)
- [ ] Profiles
- [ ] Release workflow producing signed checksums
- [ ] Documentation site: user, developer, plugin, theme, deployment guides

**Done when:** someone who has never seen the repository can download, install, and
use it.

---

## Phase 5 — Polish

- [ ] Animation pass; reduced-motion support
- [ ] Startup, memory, and search-latency budgets measured and met
- [ ] Accessibility: screen reader, keyboard-only, high contrast, UI scaling
- [ ] Crash handling with copyable diagnostics and log location
- [ ] Developer mode: module debugger, WebView inspector, console, performance monitor
- [ ] Hidden mode

---

## Not scheduled

Architecturally possible, deliberately unscheduled: cloud sync, plugin marketplace,
mobile companion, remote commands, team profiles, enterprise deployment, AI modules.

Module and settings formats are versioned so none of these require a rewrite —
but none is committed to a date either.

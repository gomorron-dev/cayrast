# Roadmap

Cayrast is built in five phases. Each ends at a tree that **builds, runs, and is
demoable** — no phase leaves the repository in a broken intermediate state.

**Current phase: 1 (Skeleton) — in progress.**

---

## Phase 0 — Foundation ✅ complete

- [x] Solution structure, seven projects, central package management
- [x] `Cayrast.Abstractions` contracts: search, commands, modules, permissions, settings, IPC
- [x] Storage path resolution (`CayrastPaths`)
- [x] Warnings-as-errors build, analyzer policy, `.editorconfig`
- [x] Test project with the first suite (`ModuleId` validation, 31 tests)
- [x] Governance docs, issue templates, CI

---

## Phase 1 — Skeleton 🔨 in progress

The application launches and responds to the hotkey.

- [ ] Composition root with `Microsoft.Extensions.DependencyInjection`
- [ ] Serilog structured logging to `%LOCALAPPDATA%\Cayrast\Logs`
- [ ] Single-instance enforcement (named mutex; second launch signals the first)
- [ ] Warm window: hidden WPF host created at startup, Mica/Acrylic backdrop, rounded corners
- [ ] WebView2 on `https://shell.cayrast.local/` via virtual host mapping
- [ ] Typed message bridge between the shell UI and Core
- [ ] Global hotkey (default **Alt+Space**), rebindable, with a graceful message if taken
- [ ] Tray icon with show/settings/quit
- [ ] Settings: typed JSON model, schema version, migration, atomic save
- [ ] Svelte 5 + Vite frontend scaffold with the theme variable system
- [ ] Multi-monitor and per-monitor DPI correctness

**Done when:** Alt+Space shows the window with no perceptible delay, Esc hides it,
settings survive a restart, and nothing is logged at error level on a clean run.

---

## Phase 2 — Engines

The launcher does its actual job, and the SDK becomes real.

- [ ] Streaming search pipeline: concurrent fan-out, per-keystroke cancellation, ranked merge
- [ ] fzf-style fuzzy scorer with match-index highlighting
- [ ] Frecency store (SQLite)
- [ ] Application indexer: Start Menu `.lnk` + UWP packages, cached, file-watcher refresh
- [ ] Filesystem provider
- [ ] Command engine, `help`, and built-ins (`calc`, `uuid`, `base64`, …)
- [ ] User-defined commands
- [ ] Settings registry, generated settings UI, settings search
- [ ] Theme engine and `.cayrast-theme` loading
- [ ] **Module system**: `.cayrast` packages, manifest validation, permission consent,
      broker, `AssemblyLoadContext` loading, `ModuleHost` sandbox, IPC transport
- [ ] `Cayrast.Sdk` v1 published

**Done when:** typing finds and launches applications, `calc 20*50` answers inline,
and a module loaded from a `.cayrast` file contributes results — sandboxed, with
permissions actually enforced.

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

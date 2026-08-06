<div align="center">

# Cayrast

**An open-source desktop command center for Windows.**

One hotkey. Search everything, run anything, and replace a folder full of small utilities.

[![Build](https://github.com/cayrast/cayrast/actions/workflows/build.yml/badge.svg)](https://github.com/cayrast/cayrast/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-8d8473.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-8d8473.svg)](https://dotnet.microsoft.com/)

</div>

---

> ### ⚠️ Status: pre-alpha — buildable, not yet released
>
> **Phases 1 and 2 are complete.** Cayrast builds, launches, registers Alt+Space,
> indexes your applications, runs commands, and loads modules. If you build from
> source it works today.
>
> There is still **no installer artifact to download** and no tagged release, and the
> official modules from Phase 3 (clipboard, file tools, QR, widgets) are not written
> yet. Follow [the roadmap](docs/ROADMAP.md) for what is done and what is next.
>
> Screenshots will be added when the interface stops changing week to week.

## What works today

Built from source, on Windows 11:

- **Alt+Space opens instantly** — the window and its WebView2 are created once at
  startup and only shown and hidden thereafter
- **Search across applications, commands, and settings**, ranked together with fuzzy
  matching, highlighted match positions, and frecency
- **284 applications indexed** on the development machine, matching what the Start
  Menu itself reports — desktop and Store apps through one code path
- **Commands** — `calc`, `uuid`, `base64`, `sha256`, `timestamp`, `json`, `help`, with
  live preview as you type
- **Generated, searchable settings** — find the transparency slider by typing "glass"
- **Modules** load from `.cayrast` packages into isolated, unloadable contexts, with
  permissions checked at a broker — though **sandboxing is not built yet**, so those
  permissions are advisory today. See [SECURITY.md](SECURITY.md).

175 tests pass, including ones that build genuinely malicious module packages and
confirm they are refused.

---

## What it is

Press **Alt+Space**. A search box appears instantly. Type, and Cayrast finds your
applications, files, clipboard history, settings, and commands — then gets out of
the way.

Underneath, it is a plugin platform. Almost every feature ships as a *module*
built on the same public SDK third-party developers use, so nothing about Cayrast
is more privileged than what you can build yourself.

### Principles

| | |
|---|---|
| **Instant** | The window is created at login and never destroyed. Alt+Space shows it — it does not launch it. |
| **Private** | No telemetry, no analytics, no account. Network access only for features that inherently need it, and only after you enable them. |
| **Modular** | Clipboard, QR, file tools, widgets — all modules, all removable, all replaceable. |
| **Yours** | Themes, layout, colors, hotkeys, and custom commands are all user-editable. |

## Planned features

Each of these is a module, not a hardcoded feature.

- **Launcher** — fuzzy search over apps, files, commands, and settings, ranked by frecency
- **Clipboard** — searchable history with pinning, OCR, and an encrypted local store
- **File Tools** — inspector, hashes, EXIF, hex viewer, strings, duplicate finder
- **Developer Tools** — JSON, Base64, regex tester, JWT decoder, timestamps, HTTP client
- **QR & Barcode** — generate and scan, including from a screen region
- **Color & Screen Tools** — picker, ruler, magnifier, region selector
- **Audio Manager** — per-application volume and device switching
- **Widgets** — clock, calendar, system monitors, notes, todo; detachable and floatable
- **Automation** — multi-step workflows bound to hotkeys or schedules
- **Window Manager** — snap layouts you can save and restore

See [docs/ROADMAP.md](docs/ROADMAP.md) for what is built and what is next.

## Installation

Not yet available. Phase 4 ships the installer and auto-updates.

> **A note on SmartScreen.** Cayrast has no code-signing certificate yet, so early
> installers will trigger a Windows SmartScreen warning. Every release is published
> with checksums you can verify. We would rather say this plainly than have you
> wonder.

## Building from source

**Requirements**

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.302 or later)
- [Node.js](https://nodejs.org/) 20 or later — for the WebView2 frontend
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — preinstalled on Windows 11
- Windows 10 (1809+) or Windows 11

**Build and run**

```bash
git clone https://github.com/cayrast/cayrast.git
```

The frontend is built separately, and the .NET build copies its output. Build it first
or the launcher starts with no interface:

```bash
cd ui/shell && npm install && npm run build
```

```bash
dotnet build Cayrast.slnx
```

```bash
dotnet run --project src/Cayrast.Shell
```

Then press **Alt+Space**.

```bash
dotnet test Cayrast.slnx
```

The solution builds warnings-clean and is expected to stay that way — warnings are
errors in this repository, and so are known-vulnerable NuGet packages.

## Architecture

Cayrast is a native .NET host rendering its UI in WebView2, with modules running
either in-process or in a sandboxed child process depending on how far you trust them.

```
Cayrast.Shell.exe ── warm hidden window ── WebView2 (shell UI, own origin)
       │                                        └─ sandboxed iframe per module UI
       ├── Cayrast.Core ............ search, commands, settings, module registry
       ├── Cayrast.Platform.Windows  every Win32/WinRT call lives here
       └── named pipe ──► Cayrast.ModuleHost.exe (one per untrusted module)
```

Two decisions shape everything else:

1. **The window is warm.** Constructing WebView2 costs ~100 ms. Paying that once at
   login instead of on every keypress is the difference between "instant" and "fast".
2. **Modules speak one IPC contract regardless of where they run.** That is what
   makes a module's trust level a setting rather than a rewrite — and what makes the
   permission system enforcement rather than documentation.

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains both in full, including the
tradeoffs we accepted and the ones we rejected.

## Creating a module

Modules are `.cayrast` packages — a manifest, an optional backend assembly, an
optional frontend, and a declared set of permissions.

```json
{
  "name": "Example Module",
  "id": "example.module",
  "version": "1.0.0",
  "author": "You",
  "description": "An example Cayrast extension",
  "permissions": ["network"],
  "entry": "main.dll"
}
```

The SDK is public and versioned, and official modules use it exactly as written — if a
built-in module can do something, so can yours. A complete worked example lives in
[`modules/Cayrast.Modules.Example`](modules/Cayrast.Modules.Example); it is packed,
installed, loaded, and exercised by the test suite on every run, so it cannot silently
rot.

Full guide: **[docs/PLUGIN_GUIDE.md](docs/PLUGIN_GUIDE.md)**.

## Creating a theme

Themes are `.cayrast-theme` files that override CSS custom properties — colours,
typography, spacing, radii, animation timings. No code, no build step, no restart.

Values are validated against an allow-list before they reach the stylesheet, because a
theme file downloaded from anywhere is untrusted input and CSS is executable enough to
matter.

Full guide: **[docs/THEME_GUIDE.md](docs/THEME_GUIDE.md)**.

## Documentation

| | |
|---|---|
| [User guide](docs/USER_GUIDE.md) | Using the launcher, commands, settings, privacy |
| [Plugin guide](docs/PLUGIN_GUIDE.md) | Writing a module |
| [Theme guide](docs/THEME_GUIDE.md) | Writing a theme |
| [Architecture](docs/ARCHITECTURE.md) | Why it is built this way, and what was rejected |
| [Roadmap](docs/ROADMAP.md) | What is done and what is next |

## Contributing

Contributions are welcome, especially while the architecture is still being shaped
and disagreement is cheap to act on. Start with [CONTRIBUTING.md](CONTRIBUTING.md).

Found a security issue? Please read [SECURITY.md](SECURITY.md) first — don't open a
public issue.

## License

[MIT](LICENSE) © Cayrast Contributors

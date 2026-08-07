<div align="center">

<img src="assets/icon.svg" alt="" width="88" height="88" />

# Cayrast

**One hotkey. Search everything, run anything.**

An open-source desktop command center for Windows — a fast launcher built on a real
plugin platform, where almost every feature is a module using the same public SDK you can.

[![Build](https://github.com/gomorron-dev/cayrast/actions/workflows/build.yml/badge.svg)](https://github.com/gomorron-dev/cayrast/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/gomorron-dev/cayrast?color=8d8473)](https://github.com/gomorron-dev/cayrast/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-8d8473)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-8d8473)](https://dotnet.microsoft.com/)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-8d8473)](#requirements)
[![Tests](https://img.shields.io/badge/tests-180%20passing-8d8473)](#testing)
[![PRs welcome](https://img.shields.io/badge/PRs-welcome-8d8473)](CONTRIBUTING.md)

[Getting started](#getting-started) ·
[Usage](#usage) ·
[Write a module](docs/PLUGIN_GUIDE.md) ·
[Write a theme](docs/THEME_GUIDE.md) ·
[Architecture](docs/ARCHITECTURE.md) ·
[Roadmap](docs/ROADMAP.md)

</div>

---

> [!NOTE]
> **v0.1.0 is released.**
>
> Cayrast launches, indexes your applications, searches files, runs commands, loads themes, and executes plugin modules. Downloads are available as an Inno Setup installer or portable zip on the [Releases](https://github.com/gomorron-dev/cayrast/releases) page.
>
> Note: **[module sandboxing is not implemented](#a-note-on-module-security)** — module permissions are advisory, not enforced. See [SECURITY.md](SECURITY.md).

---

## Contents

- [Why Cayrast](#why-cayrast)
- [What works today](#what-works-today)
- [Getting started](#getting-started)
- [Usage](#usage)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Project structure](#project-structure)
- [Extending Cayrast](#extending-cayrast)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Security](#security)
- [FAQ](#faq)
- [License](#license)

---

## Why Cayrast

Press **Alt+Space**. A search box appears instantly. Type, and it finds your
applications, commands, and settings — then gets out of the way.

The goal is not another launcher. It is a *command center* you can extend, where the
built-in features carry no privileges yours cannot have.

|  | |
|---|---|
| ⚡ **Instant** | The window and its browser are created once at login and never destroyed. Alt+Space *shows* the launcher; it does not launch it. |
| 🔒 **Private** | No telemetry, no analytics, no account. Every privacy setting defaults to the conservative choice. Network access only for features you explicitly enable. |
| 🧩 **Modular** | Clipboard, QR, file tools, widgets — all modules. Removable, replaceable, and built on the published SDK. |
| 🎨 **Yours** | Themes are plain CSS variable overrides. No code, no build step, no restart. |
| 🪟 **Native** | Acrylic backdrop, rounded corners, per-monitor DPI, tray integration. It looks like part of Windows because it uses Windows. |

### Design principles

Three rules the codebase actually holds to, not aspirations:

1. **No module can make Alt+Space slower.** Everything on the search path is
   cancellable and time-budgeted; module initialisation has a hard startup budget.
2. **Official modules use only the public SDK.** If a built-in feature needs an API,
   that API ships publicly. This is the only reliable way to keep an SDK usable.
3. **Never claim a guarantee we don't have.** Where something is designed but not
   built, the product, the docs, and the tests all say so.

---

## What works today

Verified on Windows 11, built from source:

| | |
|---|---|
| **Instant launcher** | Warm window; Alt+Space is a `ShowWindow` call |
| **Application search** | 284 apps indexed in ~1.5 s — matching exactly what `Get-StartApps` reports. Desktop *and* Store apps through one code path |
| **File search** | Bounded live walk over Desktop, Documents, Downloads, Pictures, and custom paths, excluding `.git`, `node_modules`, etc. |
| **Fuzzy matching** | fzf-style scoring with highlighted match positions, so you see *why* something matched |
| **Frecency ranking** | What you use most and most recently rises to the top. Local only |
| **Commands** | `calc`, `uuid`, `base64`, `sha256`, `timestamp`, `json`, `help` — with live preview as you type |
| **Searchable settings** | Generated from descriptors; type "glass" to find the transparency slider |
| **Module loading** | `.cayrast` packages, manifest validation, isolated and unloadable contexts |
| **Themes** | `.cayrast-theme` package loader with CSS variable sanitisation |
| **Multi-monitor** | Opens on the monitor with your cursor; correct under mixed DPI |

**180 tests pass** — including ones that build genuinely malicious module packages
(zip slip, zip bombs, CSS injection) and confirm they are refused.

> **Screenshots** will be added once the interface stops changing week to week.
> Publishing marketing shots of a UI that shifts every few days helps nobody.

---

## Getting started

### Requirements

| | |
|---|---|
| **OS** | Windows 10 (1809+) or Windows 11 |
| **[.NET 10 SDK](https://dotnet.microsoft.com/download)** | 10.0.302 or later |
| **[Node.js](https://nodejs.org/)** | 20 or later — builds the WebView2 frontend |
| **[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)** | Preinstalled on Windows 11 |

### Build and run

```bash
git clone https://github.com/gomorron-dev/cayrast.git
cd cayrast
```

Build the frontend **first** — the .NET build copies its output, and without it the
launcher starts with no interface:

```bash
cd ui/shell && npm install && npm run build && cd ../..
```

```bash
dotnet build Cayrast.slnx
```

```bash
dotnet run --project src/Cayrast.Shell
```

Then press **Alt+Space**.

### Testing

```bash
dotnet test Cayrast.slnx
```

Two suites, deliberately separate: `Cayrast.Core.Tests` is fast and deterministic;
`Cayrast.Integration.Tests` touches the real shell, enumerates your installed software,
and packs and loads a real module.

The solution builds **warnings-clean** and is expected to stay that way — warnings are
errors here, and so are known-vulnerable NuGet packages.

---

## Usage

### Keyboard

| Key | Action |
|---|---|
| <kbd>Alt</kbd>+<kbd>Space</kbd> | Show / hide the launcher |
| <kbd>↑</kbd> <kbd>↓</kbd> | Move the selection, wrapping at both ends |
| <kbd>Enter</kbd> | Run the selected result |
| <kbd>Esc</kbd> | Clear the query; press again to dismiss |

Cayrast lives in the tray and keeps running. Closing the window only hides it.

### Search

Matching is fuzzy, and where a match *starts* matters:

| You type | You get |
|---|---|
| `vsc` | **V**isual **S**tudio **C**ode |
| `chr` | **Chr**ome |
| `term` | **Term**inal |

Word initials outrank scattered letters, and an early match outranks a late one — so
`vsc` finds "Visual Studio Code" rather than "Advanced Vision Studio Codec".

### Commands

| Command | Example | Notes |
|---|---|---|
| `calc` | `calc 20*50` | Also `=`. Supports `sqrt`, `pi`, `^`, brackets |
| `uuid` | `uuid 5` | Random UUIDs |
| `base64` / `unbase64` | `base64 hello` | Encode and decode |
| `urlencode` / `urldecode` | `urlencode a b` | Percent encoding |
| `md5` `sha1` `sha256` `sha512` | `sha256 hello` | Checksums |
| `timestamp` | `timestamp 1700000000` | No argument gives the current time |
| `json` | `json {"a":1}` | Format and validate |
| `help` | `help calc` | Everything available, module commands included |

Commands with a preview show their answer **before** you press Enter. Output stays on
screen with a Copy button rather than dismissing, because the answer is usually the point.

`help` is generated from the live command registry, so a module's commands appear the
moment it loads — nothing to keep in sync.

Full details: **[User guide](docs/USER_GUIDE.md)**.

---

## Configuration

Settings are searchable — just type what you want to change. Or open them from the tray.

Nothing is written to the install directory, so uninstalling never touches your config:

```
%APPDATA%\Cayrast\          roaming — follows you to another machine
  Settings\                 settings.json, hand-editable
  Plugins\                  installed modules
  Themes\                   installed themes
  Database\                 frecency ranking

%LOCALAPPDATA%\Cayrast\     local — machine-specific, regenerable
  Logs\                     rolling, 7 days
  Cache\                    safe to delete
  WebView2\                 browser data
```

`settings.json` is meant to be hand-edited. A corrupt file will **not** stop Cayrast
starting — it is moved aside as `settings.json.corrupt-<timestamp>` and defaults are
used, so your original is preserved for inspection.

---

## Architecture

A native .NET host rendering its interface in WebView2:

```
┌─ Cayrast.Shell.exe ─────────────────────────────────────────┐
│  UI thread                                                  │
│    warm hidden WPF window ── WebView2                       │
│      https://shell.cayrast.local/       (trusted shell UI)  │
│        └─ <iframe sandbox>                                  │
│           https://mod-spotify.cayrast.local/  (module UI)   │
│                                                             │
│  Thread pool                                                │
│    Cayrast.Core ......... search, commands, settings,       │
│                           module registry, permission broker│
│    Cayrast.Platform.Windows  hotkeys, DWM, tray, indexing   │
└───────────────┬─────────────────────────────────────────────┘
                │ JSON-RPC over IpcEnvelope
    ┌───────────┴────────────┐
    │ Cayrast.ModuleHost.exe │  ⚠️ designed, not yet built
    └────────────────────────┘
```

Three decisions shape everything else:

1. **The window is warm.** Constructing WebView2 costs ~100 ms — invisible once at
   login, unacceptable on every keypress. Everything else follows from this.
2. **The UI is served from a real origin**, not `file://`. That gives it an enforceable
   Content-Security-Policy and lets each module UI get its own isolated origin, so the
   *browser* enforces frontend isolation rather than code we wrote.
3. **Modules speak one IPC contract regardless of where they run.** Making module calls
   process-boundary-shaped from day one is what turns "add a sandbox" into a hosting
   change rather than a breaking API change for already-published modules.

**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** covers all of it, including the
alternatives that were rejected and why.

---

## Project structure

```
Cayrast.slnx
├── src/
│   ├── Cayrast.Abstractions/     contracts — zero package references
│   ├── Cayrast.Sdk/              the public surface module authors compile against
│   ├── Cayrast.Core/             search, commands, settings, modules, theming
│   ├── Cayrast.Platform.Windows/ every Win32/WinRT call in the product
│   ├── Cayrast.ModuleHost/       sandbox process (stub)
│   └── Cayrast.Shell/            entry point, composition root, window
├── ui/shell/                     Svelte 5 + Vite frontend
├── modules/                      official modules, each its own project
├── tools/Cayrast.Installer/      Inno Setup script
├── tests/                        unit + integration suites
└── docs/                         architecture, roadmap, guides
```

`Platform.Windows` exists so that every `DllImport` in the codebase has exactly one
home — which keeps `Core` unit-testable against fakes.

---

## Extending Cayrast

### Modules

A `.cayrast` package is a manifest, an optional backend assembly, an optional frontend,
and a declared set of permissions:

```json
{
  "name": "Example Module",
  "id": "cayrast.example",
  "version": "1.0.0",
  "author": "You",
  "description": "An example Cayrast extension",
  "permissions": ["network"],
  "entry": "main.dll"
}
```

```csharp
public sealed class MyModule : CayrastModule
{
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        Context.RegisterCommand(
            new CommandDescriptor
            {
                Verb = "reverse",
                Summary = "Reverse some text",
                SupportsLivePreview = true,
            },
            new ReverseCommand());

        return Task.CompletedTask;
    }
}
```

A complete worked example lives in
[`modules/Cayrast.Modules.Example`](modules/Cayrast.Modules.Example). It references
**only** the public SDK, and the test suite packs, installs, loads, exercises, and
unloads it on every run — so it cannot silently rot.

**[→ Plugin guide](docs/PLUGIN_GUIDE.md)**

### Themes

```json
{
  "name": "Ember",
  "id": "example.ember",
  "base": "dark",
  "variables": {
    "--cy-accent": "#d97757",
    "--cy-bg-panel": "rgba(28, 25, 23, 0.8)"
  }
}
```

Drop it in `%APPDATA%\Cayrast\Themes\`. No code, no build, no restart.

Values are validated against an allow-list before reaching the stylesheet — a theme
file downloaded from anywhere is untrusted input, and CSS is expressive enough that
`url()` and `}` matter.

**[→ Theme guide](docs/THEME_GUIDE.md)**

---

## Roadmap

| Phase | Status |
|---|---|
| **0 — Foundation** | ✅ Solution, contracts, CI, governance |
| **1 — Skeleton** | ✅ Warm window, hotkey, tray, settings, frontend |
| **2 — Engines** | ✅ Search, commands, modules, settings registry, SDK |
| **3 — Official modules** | ⬜ Clipboard, file tools, dev tools, QR, widgets, audio |
| **4 — Ship** | 🔨 Installer written; updater and docs site pending |
| **5 — Polish** | ⬜ Animation, perf budgets, accessibility, developer mode |

**Highest-priority outstanding work:** the module sandbox
(`Cayrast.ModuleHost`), the filesystem search provider, user-defined commands, and the
theme file loader.

Full detail, including everything deliberately deferred: **[docs/ROADMAP.md](docs/ROADMAP.md)**.

---

## Contributing

Contributions are very welcome — especially now, while the architecture is still
being shaped and disagreement is cheap to act on.

- Branch from `development`, open PRs against `development`
- Open an issue first for anything non-trivial
- Warnings are errors; tests are required for anything parsing untrusted input

Read **[CONTRIBUTING.md](CONTRIBUTING.md)** and
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** before starting — the latter records
decisions and the alternatives already ruled out.

Good first areas: official modules, the filesystem search provider, accessibility
testing, and themes.

---

## Security

### A note on module security

> [!CAUTION]
> **Module sandboxing is not implemented yet.** `Cayrast.ModuleHost` is a stub, so
> every module loads **in-process** with full access to whatever you can access. The
> permissions a module declares are **advisory, not enforced**.
>
> **Treat installing a Cayrast module exactly like running any other program you
> downloaded.** Cayrast reports modules as `InProcess` and logs a warning on every load
> rather than claiming a boundary it does not have.

What *is* built is the expensive part to retrofit: the `IpcEnvelope` contract, already
carrying the WebView2 bridge in production. Adding the sandbox is a hosting change, not
a breaking API change for published modules.

### What is enforced

- **Package validation** — zip slip, zip bombs, entry-count limits, path traversal in
  manifest fields, and unrecognised permissions are all rejected, with tests that build
  real malicious archives
- **Theme validation** — allow-listed CSS value shapes; injection attempts are dropped
- **Frontend isolation** — the UI runs on a real origin under a restrictive CSP with
  `connect-src 'none'`
- **Launch targets** — an allow-list of URI schemes, because a deny-list cannot work
  when any application can register a protocol handler

### Reporting

**Do not open a public issue for a vulnerability.** Use
[private reporting](https://github.com/gomorron-dev/cayrast/security/advisories/new).
See **[SECURITY.md](SECURITY.md)** for scope and known limitations.

### Code signing

Cayrast has no code-signing certificate, so installers will trigger a Windows
SmartScreen warning until reputation builds. Releases ship with SHA-256 checksums. We
would rather state this plainly than let you discover it.

---

## FAQ

<details>
<summary><strong>How is this different from PowerToys Run or Flow Launcher?</strong></summary>

Mostly the plugin story. Cayrast is built platform-first: the built-in features use the
same public SDK third parties get, module calls are shaped for a process boundary from
day one, and settings are declarative data so the settings screen and settings search
cannot drift apart.

It is also much younger. Those projects are mature and usable today; this one is not.
</details>

<details>
<summary><strong>Why WebView2 instead of native WPF or WinUI?</strong></summary>

Themes, module UIs, and community contribution. A theme becomes CSS variable
overrides — no rebuild, no restart. A module can ship its own interface on an isolated
origin. And far more people can contribute to HTML and CSS than to XAML.

The cost is memory (~130 MB resident), which is the main thing Phase 5 has to address.
</details>

<details>
<summary><strong>Does it send anything anywhere?</strong></summary>

No. No telemetry, no analytics, no account, no crash reporting. Browser history search
is off by default. The frontend's CSP sets `connect-src 'none'`, so a stray `fetch()`
in the UI is a build-time impossibility rather than a policy promise.
</details>

<details>
<summary><strong>Why is the memory usage ~130 MB?</strong></summary>

An always-resident WebView2. It is above where it should be, it is tracked as a Phase 5
item, and the measured figure is recorded in
[ARCHITECTURE.md](docs/ARCHITECTURE.md) rather than quietly omitted.
</details>

<details>
<summary><strong>Can I use it now?</strong></summary>

If you are comfortable building from source, yes — it launches and works. If you want
to download an installer and click through it, not yet.
</details>

---

## Acknowledgements

- Scoring approach inspired by [fzf](https://github.com/junegunn/fzf), whose weights
  are well-tuned by long practical use
- Interaction model owes an obvious debt to Raycast, Spotlight, and Alfred
- Built with [.NET](https://dotnet.microsoft.com/),
  [Svelte](https://svelte.dev/), [Vite](https://vite.dev/), and
  [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)

## License

[MIT](LICENSE) © Cayrast Contributors

<div align="center">
<sub>Built in the open. Issues, ideas, and pull requests all welcome.</sub>
</div>

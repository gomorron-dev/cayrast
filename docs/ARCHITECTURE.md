# Architecture

This document records *why* Cayrast is built the way it is. Code explains what a
system does; this explains the decisions you would otherwise be tempted to undo.

---

## 1. Shape of the system

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
                │ named pipe, JSON-RPC over IpcEnvelope
    ┌───────────┴────────────┐
    │ Cayrast.ModuleHost.exe │  one per sandboxed module
    │  low-integrity token   │  collectable AssemblyLoadContext
    └────────────────────────┘
```

### Projects

| Project | Target | Responsibility |
|---|---|---|
| `Cayrast.Abstractions` | `net10.0` | Contracts only. **Zero package references** — everything depends on it, so it must never impose a dependency. |
| `Cayrast.Sdk` | `net10.0` | The public surface module authors compile against. Ships as NuGet. |
| `Cayrast.Core` | `net10.0-windows` | Engines: search, commands, settings, modules, themes, storage. Logic, never interop. |
| `Cayrast.Platform.Windows` | `net10.0-windows` | Every `DllImport` in the product. One place to audit, one place to port. |
| `Cayrast.ModuleHost` | `net10.0-windows` | Sandbox process for untrusted modules. |
| `Cayrast.Shell` | `net10.0-windows` | Entry point, composition root, window, WebView2 bridge. |

The `Core` / `Platform.Windows` split is what keeps Core unit-testable: Core depends
on interfaces, and tests supply fakes instead of a real desktop.

---

## 2. The warm window

**Decision.** The launcher window and its WebView2 are constructed once during
startup, kept hidden, and shown and hidden for the remainder of the session. They
are never destroyed and rebuilt.

**Why.** Creating a WebView2 costs roughly 100 ms. That is invisible once at login
and unacceptable on every keypress. The specification's headline requirement is that
Alt+Space feels instantaneous, and no amount of optimisation elsewhere recovers a
cold WebView2 construction.

**What it costs us.** The process is resident for the whole session, so idle memory
becomes a first-class concern rather than an afterthought. In exchange we get a
latency budget for Alt+Space measured in the time it takes to call `ShowWindow`.

**Consequences to respect.**

- Idle working set is a budget, not a statistic. Trim aggressively when hidden.
- State must reset on show — a stale query from twenty minutes ago is a bug.
- Anything expensive at startup delays login, so it must be deferred or lazy.

---

## 3. Modules: the hybrid hosting model

This is the decision the rest of the plugin system hangs from.

### The problem

The specification promises users a permission system: modules declare capabilities,
users approve them, and users can review module activity. But .NET removed Code
Access Security. **An assembly loaded into your process can P/Invoke anything**,
regardless of what its manifest claimed. An in-process permission list is a comment,
not a control.

### The options

| Approach | Permissions are | Cost |
|---|---|---|
| In-process only | Advisory | Fastest, simplest, and the promise to users is false |
| Always out-of-process | Enforced | A process per module, ~10-15 MB each, slower startup |
| **Hybrid (chosen)** | **Enforced where it matters** | Two hosting paths to maintain |

### The decision

First-party modules run **in-process**. Third-party modules run **sandboxed** in a
low-integrity child process, and may be promoted to in-process only by an explicit
user action that states plainly what is being given up.

### What makes it work

Both paths speak the identical contract — [`IpcEnvelope`](../src/Cayrast.Abstractions/Ipc/IpcEnvelope.cs).
Module code cannot tell which side of a process boundary it is on, so **trust level
is configuration, not architecture**. A module published today can be sandboxed
tomorrow without its author recompiling anything.

Had we started in-process and bolted sandboxing on later, that migration would have
been a breaking change to every published module — which in practice means it never
happens. The IPC-shaped contract is the price of keeping the option open.

**The accepted cost:** the in-process path serialises payloads it does not strictly
need to. One code path is easier to reason about and test than two, and microseconds
of JSON do not register against a millisecond-scale UI budget. If profiling ever
disagrees, the transport can special-case it behind the same type.

### Enforcement points

1. **Broker.** Modules never receive a raw `FileStream` or `HttpClient`. They ask the
   host, and the host checks `GrantedPermissions` first. This is also the natural
   place to record activity for the audit UI.
2. **Process.** For sandboxed modules the broker is backed by a low-integrity token,
   so calling Win32 directly to bypass it fails at the kernel.
3. **Origin.** Each module's UI is served from its own virtual host inside a sandboxed
   iframe, so same-origin policy — enforced by the browser, not by us — prevents any
   module UI from reading the shell's DOM or another module's storage.

---

## 4. Search

**Results stream.** `ISearchProvider.SearchAsync` returns `IAsyncEnumerable<SearchResult>`,
not a completed list. The host fans out to every provider concurrently and renders
results as they arrive.

This is the single most consequential performance decision in the subsystem. With a
`Task<IReadOnlyList<T>>`, every query would feel as slow as the slowest provider — one
filesystem walk would stall results that were already sitting in memory. Streaming
lets the command provider paint in microseconds while the file provider is still
walking.

**Cancellation is mandatory.** The token is cancelled on the next keystroke, roughly
every 100 ms at typing speed. A provider that ignores it accumulates abandoned work
and starves the thread pool within seconds.

**Ranking** combines the provider's own score, category weight, and a frecency store.
Providers rank only within their own results, because no provider can know how its
results compare to another's.

**Matching** is fzf-style subsequence scoring with bonuses for word boundaries,
camelCase humps, and path-segment starts. Providers return matched character indices
so the UI can highlight *why* something matched — which is most of what separates
fuzzy search that feels smart from fuzzy search that feels random.

---

## 5. Settings as descriptors

The specification requires settings to be searchable. That is only tractable if
settings are **data**.

Every setting — core and module alike — registers a
[`SettingDescriptor`](../src/Cayrast.Abstractions/Settings/SettingDescriptor.cs)
carrying its id, category, label, keywords, kind, bounds, and default. One registry
then drives three things at once:

- the settings screen is **generated** from descriptors;
- settings search is a **query over the same descriptors**;
- modules get settings pages identical to the built-in ones for free.

The alternative — hand-writing each page and separately maintaining a search index —
guarantees the two drift apart. Across fourteen modules, that drift is not
hypothetical.

Populate `Keywords` generously. It is what lets someone find the transparency slider
by typing "glass" or "acrylic" instead of the label a developer happened to pick.

---

## 6. Storage

User data never lives in the install directory. `Program Files` is read-only for
standard users, and writing there either fails or silently redirects into VirtualStore —
which is worse, because data appears to save and then disappears.

| Location | Contents | Why here |
|---|---|---|
| `%APPDATA%\Cayrast` | Settings, plugins, themes, commands, databases | Should follow the user to another machine |
| `%LOCALAPPDATA%\Cayrast` | Logs, caches, indexes, WebView2 data | Machine-specific, regenerable, often large |

All paths resolve through
[`CayrastPaths`](../src/Cayrast.Core/Storage/CayrastPaths.cs). Nothing composes them
by hand — backup, export, and clean uninstall each need an exact inventory of what
was written and where.

Secrets use **DPAPI**, scoped to the current user. Optional master password and
Windows Hello are planned; neither is on by default.

---

## 7. Threading

- The UI thread does window operations and nothing else.
- Search, commands, and module calls run on the thread pool.
- Module lifecycle methods are async and time-budgeted — a slow module gets reported
  as failed rather than being allowed to make the launcher feel slow.

The rule: **no module can degrade Alt+Space latency.** Anything that could is either
budgeted, cancellable, or off the hot path.

---

## 8. Deliberately deferred

Recorded so they are not mistaken for oversights:

- **Path-scoped filesystem permissions.** v1 grants profile-wide broker-mediated
  access. The manifest format reserves room, so adding scoping will not be breaking.
- **Code signing.** Certificates cost money. Releases ship with checksums until then,
  and the README says so plainly rather than letting users discover it via SmartScreen.
- **In-process serialisation bypass.** Deliberate simplicity; revisit with a profiler,
  not with intuition.
- **Marketplace, cloud sync, mobile companion.** The module and settings formats are
  versioned so these remain possible without a rewrite.

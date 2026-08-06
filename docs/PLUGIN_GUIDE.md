# Writing a Cayrast module

Almost everything in Cayrast is a module, including the features that ship with it.
This guide walks through building one.

A complete working example lives in
[`modules/Cayrast.Modules.Example`](../modules/Cayrast.Modules.Example). It is built
and loaded by the test suite on every run, so it cannot quietly rot.

---

## What a module can do

Three things, and they compose:

| Contribution | What it gives you |
|---|---|
| **Command** | A verb typed in the search box, optionally with a live preview |
| **Search provider** | Results ranked alongside applications and settings |
| **Setting** | An entry on your module's settings page, findable by search |

A module needs no backend assembly at all if it only ships a frontend. That is the
cheapest and safest kind — no code runs in the host process.

---

## The shape of a module

```
MyModule.cayrast          ← a ZIP archive
├── manifest.json         ← required, at the root
├── backend/
│   ├── main.dll          ← your assembly
│   └── main.deps.json    ← so your own dependencies resolve
├── frontend/             ← optional UI, served on its own origin
├── assets/               ← icons and images
└── documentation/
```

### manifest.json

```json
{
  "name": "Example Module",
  "id": "cayrast.example",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "One line, shown in search results and the plugin manager.",
  "permissions": [],
  "entry": "main.dll",
  "minHostVersion": "0.1.0",
  "homepage": "https://github.com/you/your-module"
}
```

| Field | Required | Notes |
|---|---|---|
| `id` | ✅ | Reverse-DNS, lowercase. Becomes a directory name and a web origin, so it is validated strictly. |
| `entry` | | Assembly inside `backend/`. Must be a plain file name — a path is rejected. |
| `ui` | | Entry HTML inside `frontend/`. Same rule. |
| `permissions` | | See below. Omit for none. |
| `minHostVersion` | | Produces "update Cayrast" rather than a confusing crash. |

---

## Your first module

Reference the SDK and nothing else:

```xml
<ItemGroup>
  <PackageReference Include="Cayrast.Sdk" Version="0.1.0" />
</ItemGroup>
```

Then:

```csharp
using Cayrast.Abstractions.Commands;
using Cayrast.Sdk;

public sealed class MyModule : CayrastModule
{
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        Context.RegisterCommand(
            new CommandDescriptor
            {
                Verb = "reverse",
                Summary = "Reverse some text",
                Usage = "reverse <text>",
                Examples = ["reverse hello"],
                SupportsLivePreview = true,
            },
            new ReverseCommand());

        return Task.CompletedTask;
    }
}
```

That is the whole thing. `help` picks the command up automatically, because it reads
the same descriptors.

### Do not block in `OnInitializeAsync`

The host applies a startup budget and reports your module as failed rather than let it
delay the launcher. If you need to build an index or open a connection, **start it and
return** — do not await it. The rule Cayrast holds to is that no module can make
Alt+Space slower.

---

## Search providers

For a provider whose results are a plain in-memory list, derive from
`SimpleSearchProvider`:

```csharp
internal sealed class MyProvider : SimpleSearchProvider
{
    public override string Id => "you.yourmodule.things";
    public override SearchCategory Category => SearchCategory.Tools;

    // Narrow this. A provider that returns true for every query is scheduled on
    // every keystroke; answering only for queries you can serve costs nothing.
    public override bool CanHandle(SearchQuery query) =>
        query.Text.StartsWith("gh ", StringComparison.OrdinalIgnoreCase);

    protected override IEnumerable<SearchResult> GetResults(SearchQuery query) =>
        _items.Select(item => new SearchResult
        {
            Id = $"thing:{item.Key}",   // stable across queries
            Title = item.Name,
            Category = SearchCategory.Tools,
            Score = 0.8,
            Actions = [ResultAction.Default("Open")],
        });
}
```

### Cancellation is not optional

Your token is cancelled on the user's **next keystroke** — roughly every 100 ms while
typing. A provider that ignores it accumulates abandoned work and starves the thread
pool within seconds. `SimpleSearchProvider` checks between every item for you; if you
implement `ISearchProvider` directly, check it yourself inside every loop.

The symptom of getting this wrong is that the whole launcher feels slow, which users
blame on Cayrast rather than on your module.

### Result ids must be stable

Derive `Id` from the target (a path, a URI, a key) — never from the query or a counter.
It is used to deduplicate results across providers and to key frecency ranking, and an
id that changes between queries silently breaks both.

---

## Permissions

Declare only what you use. Every permission you request is one more reason for someone
to decline the install.

| Permission | Grants |
|---|---|
| `filesystem` | Brokered file read and write |
| `network` | Brokered outbound requests |
| `clipboard` | Read and write the clipboard |
| `microphone` | Capture audio input |
| `audiocontrol` | Change system or per-app volume |
| `processmanagement` | Enumerate, start, terminate processes |
| `windowmanagement` | Move, resize, focus other windows |
| `screencapture` | Capture the screen |
| `shellexecute` | **Run arbitrary shell commands** |
| `notifications` | Post toasts |

**`shellexecute` is effectively full user trust.** A module that can run a shell can do
anything the user can, and the consent prompt presents it that way. If you can achieve
your goal without it, do.

### Handle a partial grant

Users may grant a subset. Check and degrade rather than failing:

```csharp
if (HasPermission(ModulePermission.Network))
{
    // fetch fresh data
}
else
{
    Log.Information("Network not granted; using cached data.");
}
```

A module that still works with less is more likely to stay installed.

### How enforcement actually works

> **⚠️ Today, it does not.** `Cayrast.ModuleHost` is a stub, so every module loads
> in-process and the permission set is advisory — a loaded assembly can P/Invoke
> anything regardless of its manifest. Cayrast reports modules as in-process and logs
> a warning on every load rather than claiming a boundary it does not have.

The design, once the sandbox lands:

- **Sandboxed modules** (the default for anything third-party) run in a separate
  low-integrity process. The permission check is backed by the operating system, so
  calling Win32 directly to bypass the broker fails at the kernel.
- **In-process modules** are fully trusted. Promotion is an explicit user action, and
  the UI states plainly what is being given up.

**Write your module as though the sandbox already exists.** Request permissions
honestly and route capabilities through `Context`. When the sandbox lands, a module
that reached around the broker will simply stop working — and because the IPC contract
is already in place, that transition needs no change from you.

---

## Packaging

Build, then zip the output with `manifest.json` at the root and your assembly under
`backend/`:

```bash
dotnet build -c Release
```

```powershell
Compress-Archive -Path bin/Release/net10.0/* -DestinationPath MyModule.cayrast
```

Cayrast validates the archive before extracting it. Packages are rejected for entries
that would write outside their own directory, for expanding past 256 MB, for more than
10,000 entries, and for declaring a permission this version does not recognise. All of
these are enforced, not advisory — see
[`ModulePackageTests`](../tests/Cayrast.Core.Tests/Modules/ModulePackageTests.cs).

---

## Testing your module

The most useful test is the one Cayrast uses on its own example:
pack it, install it through `ModuleRegistry`, enable it, and use what it contributed.
See [`ModuleLifecycleTests`](../tests/Cayrast.Integration.Tests/ModuleLifecycleTests.cs)
for the pattern — it catches the failures unit tests cannot, such as a missing
dependency that only shows up when the assembly is genuinely loaded.

---

## Common problems

**"No types could be loaded. Missing dependency?"** — your module depends on an assembly
that is not in `backend/`. Make sure `main.deps.json` ships alongside your DLL, and that
NuGet dependencies are copied to the output.

**Your `ICayrastModule` cast fails** — you shipped your own copy of `Cayrast.Sdk` or
`Cayrast.Abstractions`. Those are deliberately resolved from the host; a private copy
produces a type with the same name that is not the same type. Mark the reference
`ExcludeAssets="runtime"` or simply do not copy it.

**Your command does not appear** — check the module actually loaded. Its state is shown
in Settings under Modules, along with the failure reason if it did not.

---

## Reference

- [Architecture](ARCHITECTURE.md) — why the module system is built this way
- [Example module](../modules/Cayrast.Modules.Example) — a complete, tested module
- [Contributing](../CONTRIBUTING.md) — if you want your module to ship with Cayrast

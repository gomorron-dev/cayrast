# Contributing to Cayrast

Thanks for considering it. Cayrast is early — the architecture is still being shaped,
which makes this the cheapest time for disagreement to change the outcome.

## Before you start

**Open an issue first for anything non-trivial.** A bug fix or a typo needs no
preamble. A new module, an SDK change, or anything touching the permission model
should be discussed before you write code, so nobody spends a weekend on a design
that was already ruled out.

Check [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first — it records decisions and
the alternatives that were rejected, which may already answer your question.

## Getting set up

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.302+),
[Node.js](https://nodejs.org/) 20+, and Windows 10 1809 or later.

```bash
git clone https://github.com/gomorron-dev/cayrast.git
```

```bash
dotnet build Cayrast.slnx
```

```bash
dotnet test Cayrast.slnx
```

## Branches

| Branch | Purpose |
|---|---|
| `main` | Released, stable. Protected. |
| `development` | Integration branch. Target your PRs here. |
| `feature/*` | Your work. |

Branch from `development`, and open your PR against `development`.

## Coding standards

The build enforces most of this — warnings are errors, so a clean build means you
have already met the mechanical bar. What it cannot check:

- **Small types with one job.** If a class needs "and" to describe it, split it.
- **No hardcoded paths.** Everything goes through `CayrastPaths`.
- **No hardcoded branding.** Everything goes through `CayrastBrand`.
- **Async all the way down.** Never block the UI thread. Take a `CancellationToken`
  wherever the caller might reasonably want to stop you, and actually honour it.
- **Interop stays in `Cayrast.Platform.Windows`.** No `DllImport` anywhere else.
- **Explain the non-obvious.** Comment *why*, not *what*. If you chose an approach
  over an obvious alternative, say so — that is the comment a future reader needs.

If you must suppress an analyzer rule, do it in `.editorconfig` with a written
justification, not with a bare `#pragma`.

### Performance is a feature

Cayrast's core promise is that Alt+Space is instant. Two rules follow:

1. **Anything on the search path must be cancellable and must honour its token.**
   It gets cancelled roughly every 100 ms during typing.
2. **Anything on the startup path delays login.** Defer it, or make it lazy.
3. **Do not log per keystroke.** Log lifecycle and errors, not query progress. The
   analyzer rules that would normally police this (CA1848, CA1873) are disabled
   project-wide because they are pure ceremony everywhere else — which makes this
   convention the only thing protecting the hot path. If you genuinely need logging
   there, use a `[LoggerMessage]` source-generated delegate at that call site.

### Tests

Required for: search ranking, command parsing, module loading, permission checks,
settings migration, and anything that parses untrusted input.

That last category matters most. A `.cayrast` package is a file the user downloaded
from somewhere — its manifest gets no benefit of the doubt. See
[`ModuleIdTests`](tests/Cayrast.Core.Tests/Modules/ModuleIdTests.cs) for the expected
level of paranoia.

## Modules

Official modules use the public SDK and nothing more privileged. If your module needs
an API that does not exist, the fix is to add it to the SDK — not to reach into Core.
This constraint is what keeps the SDK genuinely usable by third parties instead of
quietly second-class.

## Pull requests

- One logical change per PR.
- Present-tense commit messages: `Add fuzzy scorer` rather than `Added fuzzy scorer`.
- Say what you changed and why. Screenshots or a short clip for UI changes.
- Update docs in the same PR — a feature is not finished when it works, it is
  finished when the next person can find out how it works.

## Security

Do not open a public issue for a vulnerability. See [SECURITY.md](SECURITY.md).

## Licence

Contributions are licensed under [MIT](LICENSE), matching the project.

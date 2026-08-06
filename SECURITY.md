# Security Policy

## Reporting a vulnerability

**Please do not open a public issue.**

Report privately through GitHub:
**Security → Advisories → Report a vulnerability** on this repository.

That channel is preferred over email — it keeps the report private until a fix ships,
and it gives you a place to follow the progress.

Please include:

- What the issue is and why it matters
- Steps to reproduce, or a proof of concept
- Affected version or commit
- Your Windows version

You can expect an acknowledgement within a few days. Cayrast is maintained by
volunteers, so please be patient — but if you hear nothing within two weeks, feel
free to nudge by opening a *non-descriptive* public issue asking a maintainer to
check their advisories.

## Supported versions

Cayrast is pre-alpha and has no releases yet. Once releases begin, only the latest
will receive security fixes until the project reaches 1.0.

## Scope

Cayrast runs local code, controls audio and windows, reads the clipboard, and loads
third-party modules. The parts most worth your attention:

| Area | What we care about |
|---|---|
| **Module sandbox** | Escaping the low-integrity `ModuleHost` process; reaching host internals past the broker |
| **Permission broker** | Performing an action without the corresponding granted permission |
| **Package loading** | Path traversal, zip-slip, or code execution while parsing a `.cayrast` file |
| **WebView2 boundary** | A module UI reaching the shell's origin, or escaping its sandboxed iframe |
| **Secret storage** | Recovering DPAPI-protected data as another user |
| **Clipboard store** | Reading encrypted clipboard history without the user's key |

### Known and accepted limitations

These are documented design positions, not vulnerabilities. Reporting them is fine,
but they are not treated as new findings.

- **⚠️ Module sandboxing is not implemented yet.** `Cayrast.ModuleHost` is a stub, so
  every module currently loads in-process and its declared permissions are advisory
  rather than enforced by the operating system. **Treat installing a Cayrast module as
  equivalent to running any other program you downloaded.** The product does not claim
  otherwise: the registry reports modules as in-process and logs a warning on every
  load. Until this lands, the permission system is a statement of intent by the module
  author, not a control.

- **In-process modules are fully trusted.** A module the user explicitly promoted to
  in-process can do anything the user can. This is stated in the UI at the moment of
  promotion. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#3-modules-the-hybrid-hosting-model).
- **`ShellExecute` permission is equivalent to full user trust.** A module that can
  run a shell can do anything the user can. The consent UI presents it as such.
- **Filesystem permission is not path-scoped in v1.** Scoping is planned; the manifest
  format reserves room for it.
- **Releases are not Authenticode-signed yet.** Certificates cost money the project
  does not have. Checksums are published with every release.

## Out of scope

- Vulnerabilities in third-party modules not distributed by this project — report
  those to their authors
- Attacks requiring administrator access or physical access to an unlocked machine
- Social engineering
- Missing hardening that has no demonstrated impact

## Disclosure

We aim to ship a fix before public disclosure and will credit you in the advisory and
changelog unless you would rather stay anonymous. If a fix is going to take a while,
we will tell you rather than go quiet.

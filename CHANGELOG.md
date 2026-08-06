# Changelog

All notable changes to Cayrast are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Because Cayrast is pre-1.0, the SDK's public surface may change between minor
versions. Such changes are always called out under **Changed** with a migration note.

---

## [Unreleased]

### Added

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

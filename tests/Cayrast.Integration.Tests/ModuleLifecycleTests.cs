using System.IO.Compression;
using Cayrast.Abstractions.Modules;
using Cayrast.Abstractions.Search;
using Cayrast.Core.Commands;
using Cayrast.Core.Modules;
using Cayrast.Core.Search;
using Cayrast.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cayrast.Integration.Tests;

/// <summary>
/// Packs, installs, loads, and uses the example module.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that justifies the plugin architecture. Everything else in the
/// module system can pass its own unit tests while the whole thing still fails to
/// actually load a module, so this exercises the real path end to end: a real assembly,
/// packed into a real <c>.cayrast</c> archive, extracted, loaded into its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, and its contributions used.
/// </para>
/// <para>
/// The example module references only <c>Cayrast.Sdk</c>, so this also verifies the
/// claim that the public SDK is sufficient — if it were not, this test would not
/// compile.
/// </para>
/// </remarks>
public sealed class ModuleLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cayrast-modules", Guid.NewGuid().ToString("N"));
    private readonly CayrastPaths _paths;

    public ModuleLifecycleTests()
    {
        _paths = new CayrastPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Finds the example module's build output by walking up to the repository root.</summary>
    private static string? FindExampleModuleOutput()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cayrast.slnx")))
            {
                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    var candidate = Path.Combine(
                        directory.FullName, "modules", "Cayrast.Modules.Example", "bin", configuration);

                    if (File.Exists(Path.Combine(candidate, "main.dll")))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Packs the example module's output into a .cayrast archive.</summary>
    private string PackExampleModule(string outputDirectory)
    {
        var packagePath = Path.Combine(_root, "example.cayrast");

        using var stream = File.Create(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        // The manifest lives at the package root; the assembly and its dependencies go
        // under backend/, which is where the loader looks.
        archive.CreateEntryFromFile(Path.Combine(outputDirectory, "manifest.json"), "manifest.json");

        foreach (var file in Directory.EnumerateFiles(outputDirectory))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            archive.CreateEntryFromFile(file, $"backend/{name}");
        }

        return packagePath;
    }

    private (ModuleRegistry Registry, SearchEngine Search, CommandEngine Commands) BuildHost()
    {
        var broker = new PermissionBroker(NullLogger<PermissionBroker>.Instance);
        var search = new SearchEngine(new NullFrecency(), NullLogger<SearchEngine>.Instance);
        var commands = new CommandEngine(NullLogger<CommandEngine>.Instance);

        var registry = new ModuleRegistry(_paths, broker, search, commands, NullLogger<ModuleRegistry>.Instance);
        return (registry, search, commands);
    }

    [Fact]
    public async Task ExampleModule_PacksInstallsLoadsAndContributes()
    {
        var output = FindExampleModuleOutput();
        Assert.SkipWhen(output is null, "The example module has not been built.");

        var package = PackExampleModule(output!);
        var (registry, search, commands) = BuildHost();
        await using var _ = registry;

        // Inspect before installing: the user must be able to see what a module asks
        // for and decline, which is impossible if the two are one step.
        var (manifest, permissions) = registry.Inspect(package);
        Assert.Equal("cayrast.example", manifest.Id);
        Assert.Equal(ModulePermission.None, permissions);

        var installed = await registry.InstallAsync(package, Token);
        Assert.Equal(ModuleState.Disabled, installed.State);

        // Asserts InProcess, not Sandboxed, because that is the truth today:
        // Cayrast.ModuleHost is a stub and modules load in-process. This assertion is
        // deliberately written to fail when the sandbox lands, so that whoever builds
        // it is forced to revisit every place the trust level is reported to a user.
        Assert.Equal(ModuleTrustLevel.InProcess, installed.TrustLevel);

        var enabled = await registry.EnableAsync(installed.Id, permissions, Token);

        // The reason is included in the message: a bare "failed to load" turns a
        // one-line fix into a debugging session.
        var reason = registry.Modules.FirstOrDefault(module => module.Id == installed.Id)?.FailureReason;
        Assert.True(enabled, $"The example module failed to load: {reason ?? "(no reason recorded)"}");

        // The module's command must now be live in the engine, indistinguishable from a
        // built-in one.
        Assert.Contains(commands.Commands, descriptor => descriptor.Verb == "reverse");

        var outcome = await commands.ExecuteAsync("reverse hello", Token);
        Assert.NotNull(outcome);
        Assert.True(outcome.Succeeded);
        Assert.Equal("olleh", outcome.Message);

        // And its search provider must contribute results through the shared pipeline.
        var query = new SearchQuery("hello", new HashSet<string> { "tools", "commands" }, 25);
        var results = new List<SearchResult>();

        await foreach (var snapshot in search.SearchAsync(query, Token))
        {
            results = [.. snapshot];
        }

        Assert.Contains(results, result => result.Id == "example:greeting");

        // Its setting is exposed for the generated settings screen.
        Assert.Contains(registry.ModuleSettings, setting => setting.Id == "example.greeting");
    }

    [Fact]
    public async Task DisablingAModule_WithdrawsEverythingItContributed()
    {
        var output = FindExampleModuleOutput();
        Assert.SkipWhen(output is null, "The example module has not been built.");

        var package = PackExampleModule(output!);
        var (registry, search, commands) = BuildHost();
        await using var _ = registry;

        var installed = await registry.InstallAsync(package, Token);
        await registry.EnableAsync(installed.Id, ModulePermission.None, Token);
        Assert.Contains(commands.Commands, descriptor => descriptor.Verb == "reverse");

        await registry.DisableAsync(installed.Id, Token);

        // A disabled module must leave nothing behind. A stale command or search
        // provider would keep answering with types from an unloaded context, which
        // either faults or silently resurrects the module.
        Assert.DoesNotContain(commands.Commands, descriptor => descriptor.Verb == "reverse");
        Assert.Empty(registry.ModuleSettings);

        var query = new SearchQuery("hello", new HashSet<string> { "tools" }, 25);
        var results = new List<SearchResult>();

        await foreach (var snapshot in search.SearchAsync(query, Token))
        {
            results = [.. snapshot];
        }

        Assert.DoesNotContain(results, result => result.Id == "example:greeting");
    }

    [Fact]
    public async Task DiscoverAsync_FindsAPreviouslyInstalledModule()
    {
        var output = FindExampleModuleOutput();
        Assert.SkipWhen(output is null, "The example module has not been built.");

        var package = PackExampleModule(output!);

        var (first, _, _) = BuildHost();
        await using (first)
        {
            await first.InstallAsync(package, Token);
        }

        // A fresh registry over the same directory, as happens on the next launch.
        var (second, _, _) = BuildHost();
        await using (second)
        {
            await second.DiscoverAsync(Token);

            Assert.Contains(second.Modules, module => module.Id.Value == "cayrast.example");
        }
    }

    [Fact]
    public async Task EnableAsync_NeverGrantsMoreThanTheManifestRequested()
    {
        var output = FindExampleModuleOutput();
        Assert.SkipWhen(output is null, "The example module has not been built.");

        var package = PackExampleModule(output!);
        var broker = new PermissionBroker(NullLogger<PermissionBroker>.Instance);
        var search = new SearchEngine(new NullFrecency(), NullLogger<SearchEngine>.Instance);
        var commands = new CommandEngine(NullLogger<CommandEngine>.Instance);

        await using var registry = new ModuleRegistry(
            _paths, broker, search, commands, NullLogger<ModuleRegistry>.Instance);

        var installed = await registry.InstallAsync(package, Token);

        // The example module declares no permissions. Even if the caller passes a full
        // set, the grant is intersected with what was declared and consented to — a
        // module can never end up with more than it asked for.
        await registry.EnableAsync(installed.Id, ModulePermission.FileSystem | ModulePermission.Network, Token);

        Assert.Equal(ModulePermission.None, broker.GetGranted(installed.Id));
        Assert.False(broker.IsGranted(installed.Id, ModulePermission.FileSystem));
    }

    private sealed class NullFrecency : IFrecencyStore
    {
        public double GetBoost(string resultId) => 0;

        public void RecordUse(string resultId)
        {
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A loaded module assembly keeps its file locked until the collectible load
            // context is actually collected, which is not deterministic. Cleanup here is
            // best-effort; a leftover temp directory must never fail a passing test.
        }
    }
}

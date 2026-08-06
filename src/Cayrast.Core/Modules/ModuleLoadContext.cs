using System.Reflection;
using System.Runtime.Loader;

namespace Cayrast.Core.Modules;

/// <summary>
/// An isolated, unloadable assembly load context for one module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate context per module.</b> Two modules may depend on different
/// versions of the same library. Loaded into the default context, whichever arrives
/// first wins and the other fails at run time with a type-load error that names neither
/// module. A private context per module lets each resolve its own dependencies.
/// </para>
/// <para>
/// <b>Why collectible.</b> Disabling, updating, or uninstalling a module should not
/// require restarting Cayrast, which is resident all day. A collectible context can be
/// unloaded — provided nothing outside it still holds a reference to a type or object
/// from inside, which is why the host only ever touches modules through interfaces
/// defined in <c>Cayrast.Abstractions</c>.
/// </para>
/// <para>
/// <b>The shared-assembly rule.</b> <c>Cayrast.Abstractions</c> and
/// <c>Cayrast.Sdk</c> are deliberately resolved from the host rather than from the
/// module directory. If a module shipped its own copy, its <c>ICayrastModule</c> would
/// be a different type from the host's despite the identical name, and the cast would
/// fail with an error that makes no sense to read.
/// </para>
/// </remarks>
internal sealed class ModuleLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// Assemblies that must come from the host, never from the module.
    /// </summary>
    private static readonly string[] SharedAssemblies =
    [
        "Cayrast.Abstractions",
        "Cayrast.Sdk",
    ];

    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>Creates a context for the module assembly at the given path.</summary>
    /// <param name="moduleId">Used as the context name, so it appears in diagnostics.</param>
    /// <param name="assemblyPath">Full path to the module's entry assembly.</param>
    public ModuleLoadContext(string moduleId, string assemblyPath)
        : base(name: $"Cayrast.Module.{moduleId}", isCollectible: true)
    {
        // Reads the module's .deps.json so its own NuGet dependencies resolve from
        // beside it rather than having to sit next to Cayrast.
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Returning null delegates to the default context, which is what keeps the
        // contract types identical on both sides of the boundary.
        if (assemblyName.Name is not null
            && Array.Exists(SharedAssemblies, shared => string.Equals(shared, assemblyName.Name, StringComparison.Ordinal)))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}

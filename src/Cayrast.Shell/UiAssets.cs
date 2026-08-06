using System.IO;

namespace Cayrast.Shell;

/// <summary>Locates the built WebView2 frontend on disk.</summary>
/// <remarks>
/// The frontend is a Vite build, not an embedded resource, so it can be served on a
/// real origin through WebView2's virtual host mapping — which is what gives the UI a
/// working Content-Security-Policy and lets module UIs be isolated by origin.
/// </remarks>
internal static class UiAssets
{
    /// <summary>
    /// Resolves the directory containing the frontend's <c>index.html</c>.
    /// </summary>
    /// <returns>The absolute path, or <see langword="null"/> if no build was found.</returns>
    /// <remarks>
    /// Two layouts are supported deliberately:
    /// <list type="bullet">
    ///   <item><b>Installed</b> — <c>ui\</c> beside the executable, produced by the build.</item>
    ///   <item><b>Development</b> — <c>ui\shell\dist\</c> found by walking up to the
    ///   repository root, so <c>dotnet run</c> works against a Vite build without a
    ///   copy step in the inner loop.</item>
    /// </list>
    /// </remarks>
    public static string? ResolveRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;

        var deployed = Path.Combine(baseDirectory, "ui");
        if (File.Exists(Path.Combine(deployed, "index.html")))
        {
            return deployed;
        }

        // Walk up looking for the repository marker, then the dev build output.
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cayrast.slnx")))
            {
                var development = Path.Combine(directory.FullName, "ui", "shell", "dist");
                return File.Exists(Path.Combine(development, "index.html")) ? development : null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

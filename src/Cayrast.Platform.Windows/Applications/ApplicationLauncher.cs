using System.Diagnostics;
using Cayrast.Abstractions.Applications;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows.Applications;

/// <summary>Starts applications and opens shell targets.</summary>
public sealed class ApplicationLauncher(ILogger<ApplicationLauncher> logger) : IApplicationLauncher
{
    /// <inheritdoc />
    public bool Launch(InstalledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        try
        {
            if (application.LaunchViaAppsFolder)
            {
                // An AppUserModelID is not a path and cannot be started directly.
                // Routing through the AppsFolder shell namespace is the supported way
                // to activate one, and it works for both Store apps and desktop
                // applications that register a plain identity.
                return Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{application.LaunchId}",
                    UseShellExecute = false,
                });
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = application.LaunchId,

                // Required so that shortcuts, registered file types, and elevation
                // manifests are all honoured by the shell rather than bypassed.
                UseShellExecute = true,
            };

            var workDir = Path.GetDirectoryName(application.LaunchId);
            if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
            {
                startInfo.WorkingDirectory = workDir;
            }

            return Start(startInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not launch '{Application}'.", application.Name);
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Security.</b> Handing a string to <c>UseShellExecute</c> lets the shell decide
    /// what to run, which makes this a genuinely dangerous method to call with
    /// unvalidated input: <c>file://</c>, UNC paths, and registered protocol handlers
    /// all resolve here. Callers must pass a target the user chose from a result they
    /// can see — never a value assembled from module output or file contents.
    /// </para>
    /// <para>
    /// Only http, https, mailto, and existing filesystem paths are accepted. Anything
    /// else is refused, so a crafted result cannot invoke an arbitrary protocol handler.
    /// </para>
    /// </remarks>
    public bool Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (!IsAllowedTarget(target))
        {
            logger.LogWarning("Refused to open '{Target}': not an allowed target type.", target);
            return false;
        }

        try
        {
            return Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open '{Target}'.", target);
            return false;
        }
    }

    /// <summary>Restricts what the shell is allowed to be handed.</summary>
    private static bool IsAllowedTarget(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            // Deliberately an allow-list. A deny-list of dangerous schemes cannot work:
            // any application may register a new protocol handler at any time.
            return uri.Scheme is "http" or "https" or "mailto";
        }

        return File.Exists(target) || Directory.Exists(target);
    }

    /// <inheritdoc />
    public bool RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        try
        {
            // Quoted because paths routinely contain spaces, and /select, needs the
            // path as one argument.
            return Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not reveal '{Path}'.", path);
            return false;
        }
    }

    private bool Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);

        // A null return is normal when the shell handed the request to an already
        // running instance, so it is not treated as failure.
        logger.LogDebug("Started '{FileName}'.", startInfo.FileName);
        return true;
    }
}

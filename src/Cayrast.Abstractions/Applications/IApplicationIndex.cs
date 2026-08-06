namespace Cayrast.Abstractions.Applications;

/// <summary>An application the user can launch.</summary>
/// <param name="Name">Display name, e.g. "Visual Studio Code".</param>
/// <param name="LaunchId">
/// Either a filesystem path or an AppUserModelID, depending on
/// <paramref name="LaunchViaAppsFolder"/>.
/// </param>
/// <param name="IconSource">
/// Path an icon can be extracted from, or <see langword="null"/> when the application
/// has no file on disk to read one from.
/// </param>
/// <param name="LaunchViaAppsFolder">
/// Whether <paramref name="LaunchId"/> is an AppUserModelID that must be activated
/// through the shell's AppsFolder rather than started as a file.
/// </param>
/// <remarks>
/// <para>
/// <b>This flag is about launch mechanism, not packaging.</b> The distinction is easy
/// to get wrong: not every AppUserModelID belongs to a Store app. Some desktop
/// installers register a plain identity such as <c>Anysphere.Cursor</c> — no bundle
/// suffix, no <c>!</c>, and no path either. Treating "packaged" as the test and
/// anything else as a path makes those applications fail to launch, with an error that
/// points at the shell rather than at the assumption.
/// </para>
/// <para>
/// The reliable test is simply whether the identifier is a path.
/// </para>
/// </remarks>
public sealed record InstalledApplication(string Name, string LaunchId, string? IconSource, bool LaunchViaAppsFolder);

/// <summary>Enumerates installed applications and keeps the list current.</summary>
public interface IApplicationIndex
{
    /// <summary>Applications known right now. Empty until the first scan completes.</summary>
    IReadOnlyList<InstalledApplication> Applications { get; }

    /// <summary>Raised after the index changes, so search can invalidate caches.</summary>
    event EventHandler? Updated;

    /// <summary>Builds the index and begins watching for installs and uninstalls.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Rebuilds the index immediately.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Starts applications and opens files, folders, and URLs.</summary>
public interface IApplicationLauncher
{
    /// <summary>Launches an indexed application.</summary>
    /// <returns><see langword="false"/> if it could not be started.</returns>
    bool Launch(InstalledApplication application);

    /// <summary>Opens a path or URL with its registered handler.</summary>
    /// <remarks>
    /// The target is passed to the shell, which decides what to do with it. Callers
    /// must never build one from unvalidated text — see the implementation's remarks.
    /// </remarks>
    bool Open(string target);

    /// <summary>Selects a file or folder in Explorer without opening it.</summary>
    bool RevealInExplorer(string path);
}

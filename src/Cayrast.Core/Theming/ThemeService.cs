using System.Text.Json;
using System.Text.Json.Serialization;
using Cayrast.Abstractions;
using Cayrast.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Theming;

/// <summary>Source-generated JSON metadata for theme files.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(CayrastTheme))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext;

/// <summary>A theme that has been read, validated, and is safe to apply.</summary>
/// <param name="Theme">The theme as authored.</param>
/// <param name="SafeVariables">Only the variables that passed validation.</param>
/// <param name="RejectedVariables">Variable names that were dropped, for reporting.</param>
public sealed record LoadedTheme(
    CayrastTheme Theme,
    IReadOnlyDictionary<string, string> SafeVariables,
    IReadOnlyList<string> RejectedVariables);

/// <summary>Discovers and loads installed themes.</summary>
public interface IThemeService
{
    /// <summary>Themes found on disk, keyed by id.</summary>
    IReadOnlyList<LoadedTheme> Themes { get; }

    /// <summary>Rescans the themes directory.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a loaded theme by id, or <see langword="null"/>.</summary>
    LoadedTheme? Find(string themeId);
}

/// <summary>
/// Reads <c>.cayrast-theme</c> files from the themes directory.
/// </summary>
/// <remarks>
/// <para>
/// Every theme passes through <see cref="ThemeValidator"/> before it is exposed. A
/// theme file is downloaded from anywhere and its values are injected into the
/// stylesheet as custom properties, which makes an unvalidated value a CSS injection
/// vector — see the validator for what that means in practice.
/// </para>
/// <para>
/// A malformed theme is skipped with a logged reason rather than failing the scan.
/// One broken file in the directory must not cost the user every other theme they
/// installed.
/// </para>
/// </remarks>
public sealed class ThemeService(ICayrastPaths paths, ILogger<ThemeService> logger) : IThemeService
{
    /// <summary>
    /// Largest theme file that will be read.
    /// </summary>
    /// <remarks>
    /// A theme is a small JSON object of colour tokens. Anything approaching a megabyte
    /// is either a mistake or an attempt to exhaust memory during the startup scan.
    /// </remarks>
    private const long MaxThemeBytes = 512 * 1024;

    private volatile IReadOnlyList<LoadedTheme> _themes = [];

    /// <inheritdoc />
    public IReadOnlyList<LoadedTheme> Themes => _themes;

    /// <inheritdoc />
    public LoadedTheme? Find(string themeId) =>
        _themes.FirstOrDefault(theme => string.Equals(theme.Theme.Id, themeId, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = paths.Themes;
        Directory.CreateDirectory(directory);

        var loaded = new List<LoadedTheme>();

        foreach (var file in Directory.EnumerateFiles(directory, $"*{CayrastBrand.ThemePackageExtension}"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var theme = await TryLoadAsync(file, cancellationToken);
            if (theme is not null)
            {
                loaded.Add(theme);
            }
        }

        _themes = loaded;
        logger.LogInformation("Loaded {Count} themes.", loaded.Count);
    }

    private async Task<LoadedTheme?> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxThemeBytes)
            {
                logger.LogWarning("Skipping theme '{File}': it is {Size} bytes, above the {Limit} byte limit.",
                    info.Name, info.Length, MaxThemeBytes);
                return null;
            }

            await using var stream = File.OpenRead(path);
            var theme = await JsonSerializer.DeserializeAsync(stream, ThemeJsonContext.Default.CayrastTheme, cancellationToken);

            if (theme is null || string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(theme.Name))
            {
                logger.LogWarning("Skipping theme '{File}': it has no id or name.", Path.GetFileName(path));
                return null;
            }

            var safe = ThemeValidator.Sanitise(theme, out var rejected);

            if (rejected.Count > 0)
            {
                // Named individually so the theme's author can fix them. "Some values
                // were rejected" would be useless feedback.
                logger.LogWarning(
                    "Theme '{Theme}' had {Count} variables rejected by validation: {Names}",
                    theme.Name, rejected.Count, string.Join(", ", rejected));
            }

            return new LoadedTheme(
                theme with { Base = ThemeValidator.NormaliseBase(theme.Base) },
                safe,
                rejected);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Skipping unreadable theme '{File}'.", Path.GetFileName(path));
            return null;
        }
    }
}

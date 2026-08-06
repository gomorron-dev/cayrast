using System.Text.Json;
using Cayrast.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Settings;

/// <summary>
/// JSON-backed settings with atomic writes, debounced saves, and corruption recovery.
/// </summary>
/// <remarks>
/// <para>
/// Three properties matter more than the storage format:
/// </para>
/// <list type="number">
///   <item><b>A corrupt file never blocks startup.</b> An unreadable settings file is
///   moved aside and defaults are used. Refusing to start because of a malformed
///   config would be the worst possible response for a tool the user launches
///   everything else from.</item>
///   <item><b>Writes are atomic.</b> Content goes to a temporary file which then
///   replaces the target in one move, so a crash or power loss mid-write cannot leave
///   a half-written file.</item>
///   <item><b>Writes are debounced.</b> Dragging an opacity slider changes settings
///   continuously; without coalescing that is hundreds of disk writes a second.</item>
/// </list>
/// </remarks>
public sealed class SettingsService : ISettingsService, IAsyncDisposable
{
    /// <summary>How long to coalesce changes before writing.</summary>
    /// <remarks>
    /// Long enough to absorb a slider drag, short enough that a change is on disk
    /// before a user could plausibly kill the process after making it.
    /// </remarks>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _filePath;

    private CayrastSettings _current = new();
    private CancellationTokenSource? _pendingSave;
    private bool _disposed;

    /// <summary>Creates the service. Does not read from disk; call <see cref="LoadAsync"/>.</summary>
    public SettingsService(ICayrastPaths paths, ILogger<SettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _logger = logger;
        _filePath = Path.Combine(paths.Settings, "settings.json");
    }

    /// <inheritdoc />
    public CayrastSettings Current => _current;

    /// <inheritdoc />
    public event EventHandler<CayrastSettings>? Changed;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No settings file yet; starting with defaults.");
            _current = new CayrastSettings();
            await WriteAsync(_current, cancellationToken);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync(
                stream, SettingsJsonContext.Default.CayrastSettings, cancellationToken);

            if (loaded is null)
            {
                throw new JsonException("Settings file deserialised to null.");
            }

            // Normalising is not optional. The file is hand-editable and may have been
            // written by an older version, so it can contain explicit nulls, missing
            // sections, and out-of-range numbers — none of which the type system stops.
            _current = Migrate(loaded).Normalized();
            _logger.LogInformation("Settings loaded (schema version {Version}).", _current.SchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preserve the unreadable file rather than overwriting it: it may be the
            // only copy of a configuration the user spent time on, and it is the only
            // evidence of what went wrong.
            await QuarantineCorruptFileAsync(ex);
            _current = new CayrastSettings();
        }

        Changed?.Invoke(this, _current);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Func<CayrastSettings, CayrastSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Normalised on the way in as well: the settings UI is web code and can send
        // any number at all, so clamping only on load would let a bad value take
        // effect for the rest of the session.
        var updated = update(_current).Normalized();
        _current = updated;
        Changed?.Invoke(this, updated);

        ScheduleSave(updated);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Cancel the pending debounce so it cannot fire a redundant second write.
        await CancelPendingSaveAsync();
        await WriteAsync(_current, cancellationToken);
    }

    private void ScheduleSave(CayrastSettings settings)
    {
        _pendingSave?.Cancel();
        _pendingSave?.Dispose();

        var cts = new CancellationTokenSource();
        _pendingSave = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounce, cts.Token);
                await WriteAsync(settings, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer change; the newer one will write instead.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings.");
            }
        }, CancellationToken.None);
    }

    private async Task WriteAsync(CayrastSettings settings, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Write to a sibling temporary file, then replace in a single move. A
            // crash before the move leaves the previous settings intact; a crash after
            // it leaves the new ones. There is no window where the file is partial.
            var temporaryPath = _filePath + ".tmp";

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, settings, SettingsJsonContext.Default.CayrastSettings, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            _logger.LogDebug("Settings written to {Path}.", _filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed save must not crash the application. The user keeps working
            // with the in-memory value and sees the failure in the log.
            _logger.LogError(ex, "Could not write settings to {Path}.", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task QuarantineCorruptFileAsync(Exception cause)
    {
        var quarantinePath = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        try
        {
            File.Move(_filePath, quarantinePath, overwrite: true);
            _logger.LogError(
                cause,
                "Settings file was unreadable and has been moved to {Path}. Defaults are in use.",
                quarantinePath);
        }
        catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(moveFailure, "Settings file was unreadable and could not be moved aside.");
        }

        await Task.CompletedTask;
    }

    /// <summary>Upgrades a settings tree written by an older version.</summary>
    /// <remarks>
    /// Migrations run in sequence, each stepping the version by one, so a file from any
    /// prior version reaches the current shape by composition rather than needing a
    /// dedicated path per source version.
    /// </remarks>
    private CayrastSettings Migrate(CayrastSettings settings)
    {
        if (settings.SchemaVersion > CayrastSettings.CurrentSchemaVersion)
        {
            // Written by a newer Cayrast, most likely after a downgrade. Reading it
            // with older rules risks silently discarding fields we do not understand,
            // so use defaults and leave the file untouched for the newer version.
            _logger.LogWarning(
                "Settings file uses schema version {FileVersion}, newer than the supported {SupportedVersion}. Using defaults without modifying the file.",
                settings.SchemaVersion,
                CayrastSettings.CurrentSchemaVersion);
            return new CayrastSettings();
        }

        var migrated = settings;

        // Future migrations chain here, for example:
        //   if (migrated.SchemaVersion < 2) { migrated = MigrateV1ToV2(migrated); }

        return migrated with { SchemaVersion = CayrastSettings.CurrentSchemaVersion };
    }

    private async Task CancelPendingSaveAsync()
    {
        if (_pendingSave is null)
        {
            return;
        }

        await _pendingSave.CancelAsync();
        _pendingSave.Dispose();
        _pendingSave = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // A debounced change that never reached disk would be silently lost, which
        // users experience as "it forgot my setting".
        await FlushAsync(CancellationToken.None);
        _writeLock.Dispose();
    }
}

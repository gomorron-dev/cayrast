namespace Cayrast.Core.Settings;

/// <summary>Loads, persists, and broadcasts changes to user settings.</summary>
public interface ISettingsService
{
    /// <summary>The current settings. Never <see langword="null"/>, even before loading.</summary>
    /// <remarks>
    /// Backed by an immutable record, so a caller that captures this reference holds a
    /// stable snapshot and will not observe a torn value mid-operation.
    /// </remarks>
    CayrastSettings Current { get; }

    /// <summary>Raised after settings change, carrying the new value.</summary>
    event EventHandler<CayrastSettings>? Changed;

    /// <summary>Reads settings from disk, falling back to defaults if unreadable.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a change and schedules a write.
    /// </summary>
    /// <param name="update">Receives the current settings and returns the new value.</param>
    /// <param name="cancellationToken">Cancels the write, not the in-memory change.</param>
    /// <remarks>
    /// Writes are debounced, so dragging a slider produces one file write rather than
    /// one per pixel. Call <see cref="FlushAsync"/> to force an immediate write.
    /// </remarks>
    Task UpdateAsync(Func<CayrastSettings, CayrastSettings> update, CancellationToken cancellationToken = default);

    /// <summary>Writes any pending change immediately.</summary>
    /// <remarks>Must be called during shutdown so a debounced change is not lost.</remarks>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

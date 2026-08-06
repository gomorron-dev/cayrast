using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cayrast.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Search;

/// <summary>Persisted usage record for one result.</summary>
/// <param name="UseCount">Times the user has chosen it.</param>
/// <param name="LastUsedUtc">When they last chose it.</param>
public sealed record FrecencyEntry(int UseCount, DateTime LastUsedUtc);

/// <summary>Source-generated JSON metadata for the frecency file.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Dictionary<string, FrecencyEntry>))]
internal sealed partial class FrecencyJsonContext : JsonSerializerContext;

/// <summary>
/// Tracks how often and how recently each result is chosen, and turns that into a
/// ranking boost.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model.</b> Each use contributes a weight that decays with age, so the score
/// is a sum of exponentially-decayed uses rather than a raw count. Frequency alone
/// would entrench whatever someone opened most last year; recency alone would forget
/// their daily tools after one unusual afternoon. Decayed frequency handles both, and
/// adapts when habits change.
/// </para>
/// <para>
/// <b>Why a JSON file rather than a database.</b> This is a few hundred entries of
/// two numbers each, read entirely into memory at startup and never queried
/// relationally. A database would add a dependency, a schema, and a migration story to
/// solve a problem a dictionary already solves.
/// </para>
/// </remarks>
public sealed class FrecencyStore : IFrecencyStore
{
    /// <summary>
    /// How long until a single use decays to half its weight.
    /// </summary>
    /// <remarks>
    /// Two weeks tracks a working rhythm: a tool used daily stays near the top, and one
    /// abandoned a month ago drops away without vanishing outright.
    /// </remarks>
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(14);

    /// <summary>
    /// Uses beyond this contribute nothing further.
    /// </summary>
    /// <remarks>
    /// Prevents one heavily-used item from permanently monopolising the top of every
    /// result list regardless of what was typed.
    /// </remarks>
    private const int SaturationUseCount = 20;

    /// <summary>Entries kept before the least valuable are pruned.</summary>
    private const int MaxEntries = 2000;

    private readonly ConcurrentDictionary<string, FrecencyEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FrecencyStore> _logger;
    private readonly string _filePath;
    private readonly TimeProvider _time;

    /// <summary>Creates the store. Call <see cref="LoadAsync"/> before use.</summary>
    /// <param name="paths">Resolves where the file lives.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <param name="timeProvider">
    /// Injected so decay behaviour can be tested without waiting weeks.
    /// </param>
    public FrecencyStore(ICayrastPaths paths, ILogger<FrecencyStore> logger, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _filePath = Path.Combine(paths.Database, "frecency.json");
    }

    /// <inheritdoc />
    public double GetBoost(string resultId)
    {
        if (string.IsNullOrEmpty(resultId) || !_entries.TryGetValue(resultId, out var entry))
        {
            return 0;
        }

        var ageDays = (_time.GetUtcNow().UtcDateTime - entry.LastUsedUtc).TotalDays;

        // A clock that moved backwards (timezone change, NTP correction) must not
        // produce a boost above 1.0 and outrank everything.
        if (ageDays < 0)
        {
            ageDays = 0;
        }

        var decay = Math.Pow(0.5, ageDays / HalfLife.TotalDays);
        var frequency = Math.Min(entry.UseCount, SaturationUseCount) / (double)SaturationUseCount;

        return Math.Clamp(frequency * decay, 0.0, 1.0);
    }

    /// <inheritdoc />
    public void RecordUse(string resultId)
    {
        if (string.IsNullOrEmpty(resultId))
        {
            return;
        }

        var now = _time.GetUtcNow().UtcDateTime;

        _entries.AddOrUpdate(
            resultId,
            _ => new FrecencyEntry(1, now),
            (_, existing) => existing with { UseCount = existing.UseCount + 1, LastUsedUtc = now });

        if (_entries.Count > MaxEntries)
        {
            Prune();
        }
    }

    /// <summary>Drops the least valuable entries once the store grows too large.</summary>
    /// <remarks>
    /// Prunes by current boost rather than by raw count, so a rarely-but-recently used
    /// item is kept over a frequently-but-long-ago used one — matching how the score
    /// itself is computed.
    /// </remarks>
    private void Prune()
    {
        var doomed = _entries
            .OrderBy(pair => GetBoost(pair.Key))
            .Take(_entries.Count - (MaxEntries / 2))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in doomed)
        {
            _entries.TryRemove(key, out _);
        }

        _logger.LogDebug("Pruned {Count} frecency entries.", doomed.Count);
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync(
                stream, FrecencyJsonContext.Default.DictionaryStringFrecencyEntry, cancellationToken);

            if (loaded is null)
            {
                return;
            }

            foreach (var (key, value) in loaded)
            {
                _entries[key] = value;
            }

            _logger.LogDebug("Loaded {Count} frecency entries.", _entries.Count);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Frecency is an optimisation, not data the user would miss. Losing it
            // degrades ranking for a few days; failing to start would be far worse.
            _logger.LogWarning(ex, "Could not read frecency data; ranking will start from scratch.");
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Same atomic write as settings: a crash mid-write must not leave a
            // half-written file that fails to parse on the next launch.
            var temporaryPath = _filePath + ".tmp";
            var snapshot = new Dictionary<string, FrecencyEntry>(_entries, StringComparer.OrdinalIgnoreCase);

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, snapshot, FrecencyJsonContext.Default.DictionaryStringFrecencyEntry, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save frecency data.");
        }
    }
}

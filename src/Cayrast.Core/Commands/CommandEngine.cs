using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Cayrast.Abstractions.Commands;
using Cayrast.Abstractions.Search;
using Cayrast.Core.Search;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Commands;

/// <summary>Registers, discovers, and runs commands.</summary>
public interface ICommandEngine
{
    /// <summary>Every registered command, for <c>help</c> and for documentation.</summary>
    IReadOnlyCollection<CommandDescriptor> Commands { get; }

    /// <summary>Registers a command. Replaces any existing one with the same verb.</summary>
    void Register(CommandDescriptor descriptor, ICommandHandler handler);

    /// <summary>Removes every command contributed by a module.</summary>
    void UnregisterModule(string moduleId);

    /// <summary>Runs the command named at the start of the input, if there is one.</summary>
    ValueTask<CommandOutcome?> ExecuteAsync(string input, CancellationToken cancellationToken);
}

/// <summary>
/// The command registry, dispatcher, and search provider.
/// </summary>
/// <remarks>
/// <para>
/// Commands are exposed through <see cref="ISearchProvider"/> rather than being
/// special-cased in the launcher, so they rank alongside applications and files
/// through the same pipeline. That also means a module's commands behave exactly like
/// built-in ones with no extra work.
/// </para>
/// <para>
/// Descriptors drive discovery: <c>help</c> enumerates them, and search matches
/// against verbs, aliases, summaries, and examples. A user who half-remembers
/// "something about base 64" finds it without knowing the verb.
/// </para>
/// </remarks>
public sealed class CommandEngine(ILogger<CommandEngine> logger) : ICommandEngine, ISearchProvider
{
    private readonly ConcurrentDictionary<string, Registration> _byVerb = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Id => "cayrast.commands";

    /// <inheritdoc />
    public SearchCategory Category => SearchCategory.Commands;

    /// <inheritdoc />
    public IReadOnlyCollection<CommandDescriptor> Commands =>
        _byVerb.Values
            .Select(registration => registration.Descriptor)
            .DistinctBy(descriptor => descriptor.Verb, StringComparer.OrdinalIgnoreCase)
            .OrderBy(descriptor => descriptor.Verb, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc />
    public void Register(CommandDescriptor descriptor, ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Verb);

        var registration = new Registration(descriptor, handler);
        _byVerb[descriptor.Verb] = registration;

        // Aliases resolve to the same registration, so "=" and "calc" are genuinely
        // the same command rather than two that must be kept in step.
        foreach (var alias in descriptor.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _byVerb[alias] = registration;
            }
        }

        logger.LogDebug("Registered command '{Verb}'.", descriptor.Verb);
    }

    /// <inheritdoc />
    public void UnregisterModule(string moduleId)
    {
        var doomed = _byVerb
            .Where(pair => string.Equals(pair.Value.Descriptor.OwnerModuleId, moduleId, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var verb in doomed)
        {
            _byVerb.TryRemove(verb, out _);
        }

        if (doomed.Count > 0)
        {
            logger.LogInformation("Removed {Count} commands belonging to module '{Module}'.", doomed.Count, moduleId);
        }
    }

    /// <inheritdoc />
    public async ValueTask<CommandOutcome?> ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        var invocation = Parse(input);
        if (invocation is null || !_byVerb.TryGetValue(invocation.Verb, out var registration))
        {
            return null;
        }

        try
        {
            return await registration.Handler.ExecuteAsync(invocation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A command reports expected failure by returning CommandOutcome.Failure.
            // Reaching here means a genuine bug, so it is logged as one — but the user
            // still gets a usable message instead of a silent no-op.
            logger.LogError(ex, "Command '{Verb}' threw.", invocation.Verb);
            return CommandOutcome.Failure($"'{invocation.Verb}' failed unexpectedly. See the Cayrast log for details.");
        }
    }

    /// <inheritdoc />
    public bool CanHandle(SearchQuery query) => !query.IsEmpty;

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var invocation = Parse(query.Text);
        if (invocation is null)
        {
            yield break;
        }

        // An exact verb match is the strongest possible signal, and its live preview is
        // most of the value of a command like `calc`: seeing the answer before pressing
        // Enter is what makes it feel instant rather than transactional.
        if (_byVerb.TryGetValue(invocation.Verb, out var exact))
        {
            yield return await BuildExactResultAsync(exact, invocation, cancellationToken);
        }

        // Fuzzy matches let commands be discovered by users who do not know the verb.
        foreach (var result in FindDiscoverableCommands(query, invocation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return result;
        }
    }

    private async Task<SearchResult> BuildExactResultAsync(
        Registration registration,
        CommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var descriptor = registration.Descriptor;
        string? preview = null;

        if (descriptor.SupportsLivePreview)
        {
            try
            {
                preview = await registration.Handler.PreviewAsync(invocation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Preview runs on every keystroke against half-typed input, so failure
                // is completely expected — "2+" is not yet a valid expression. Falling
                // back to the summary keeps the row stable while the user types.
                logger.LogDebug(ex, "Preview for '{Verb}' failed; showing the summary instead.", descriptor.Verb);
            }
        }

        return new SearchResult
        {
            Id = $"command:{descriptor.Verb}",
            Title = preview ?? descriptor.Summary,
            Subtitle = preview is not null ? $"{descriptor.Verb} — {descriptor.Summary}" : descriptor.Usage,
            Category = SearchCategory.Commands,
            Icon = IconReference.Glyph("command"),

            // An exact verb match should sit at the top: the user typed the command's
            // name, which leaves little room for doubt about intent.
            Score = 1.0,
            Actions = [ResultAction.Default(preview is not null ? "Copy result" : "Run")],
            Tag = descriptor.Verb,
        };
    }

    private IEnumerable<SearchResult> FindDiscoverableCommands(SearchQuery query, CommandInvocation invocation)
    {
        foreach (var descriptor in Commands)
        {
            // The exact match was already emitted above.
            if (string.Equals(descriptor.Verb, invocation.Verb, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var verbMatch = FuzzyMatcher.Match(descriptor.Verb, query.Text);
            var summaryMatch = FuzzyMatcher.Match(descriptor.Summary, query.Text);

            // The verb is a far stronger signal than prose, so a summary hit is
            // discounted heavily; without that, a common word in a description would
            // outrank the command actually named by what was typed.
            var score = Math.Max(
                verbMatch.Matched ? verbMatch.Score : 0,
                summaryMatch.Matched ? summaryMatch.Score * 0.4 : 0);

            if (score <= 0)
            {
                continue;
            }

            yield return new SearchResult
            {
                Id = $"command:{descriptor.Verb}",
                Title = descriptor.Verb,
                Subtitle = descriptor.Summary,
                Category = SearchCategory.Commands,
                Icon = IconReference.Glyph("command"),
                Score = score,
                TitleMatchIndices = verbMatch.Matched ? verbMatch.MatchedIndices : [],
                Actions = [ResultAction.Default("Run")],
                Tag = descriptor.Verb,
            };
        }
    }

    /// <summary>Splits raw input into a verb and its arguments.</summary>
    private static CommandInvocation? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.TrimStart();
        var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return separator < 0
            ? new CommandInvocation(trimmed, string.Empty)
            : new CommandInvocation(trimmed[..separator], trimmed[(separator + 1)..].Trim());
    }

    private sealed record Registration(CommandDescriptor Descriptor, ICommandHandler Handler);
}

using System.Text.Json;
using Cayrast.Abstractions.Applications;
using Cayrast.Abstractions.Search;
using Cayrast.Core.Commands;
using Cayrast.Core.Search;
using Cayrast.Core.Settings;
using Cayrast.Shell.Bridge;
using Microsoft.Extensions.Logging;

namespace Cayrast.Shell;

/// <summary>Payload for a <c>search.query</c> request.</summary>
/// <param name="Text">What the user has typed.</param>
public sealed record SearchRequest(string Text);

/// <summary>Payload for a <c>result.activate</c> request.</summary>
/// <param name="ResultId">Identifier of the chosen result.</param>
/// <param name="ActionId">Which of its actions to run.</param>
public sealed record ActivateRequest(string ResultId, string ActionId);

/// <summary>
/// Bridges the streaming search engine to the request/response frontend.
/// </summary>
/// <remarks>
/// <para>
/// The engine emits ranked snapshots as providers report in, but the bridge is
/// request/response. Rather than throw away progressive rendering by waiting for the
/// final list, each snapshot is pushed to the interface as a <c>search.partial</c>
/// event and the request completes with the last one. The frontend paints partials
/// immediately and the response settles the query.
/// </para>
/// <para>
/// Every query supersedes the one before it. Holding the previous cancellation source
/// here is what stops abandoned work from piling up: at typing speed a new query
/// arrives roughly every 100 ms, and without cancellation each would run to completion
/// against results nobody will ever see.
/// </para>
/// </remarks>
public sealed class SearchCoordinator(
    ISearchEngine engine,
    ICommandEngine commands,
    ISettingsService settings,
    IFrecencyStore frecency,
    IApplicationLauncher launcher,
    WebMessageBridge bridge,
    ILogger<SearchCoordinator> logger) : IDisposable
{
    private readonly Lock _queryLock = new();
    private CancellationTokenSource? _currentQuery;

    /// <summary>Most recent results, kept so activation can resolve an id to a result.</summary>
    /// <remarks>
    /// The frontend sends back only an id. Re-running the search to find what it refers
    /// to would be wasteful and could resolve to something different if the index moved
    /// underneath, so the last snapshot is retained instead.
    /// </remarks>
    private IReadOnlyList<SearchResult> _lastResults = [];

    /// <summary>
    /// The text behind <see cref="_lastResults"/>.
    /// </summary>
    /// <remarks>
    /// A command result carries only its verb, but running it needs the arguments too —
    /// <c>calc 20*50</c> is useless as just <c>calc</c>. Retaining the full query is how
    /// activation recovers them.
    /// </remarks>
    private string _lastQueryText = string.Empty;

    private bool _disposed;

    /// <summary>Registers the search-related bridge channels.</summary>
    public void RegisterChannels()
    {
        bridge.Register("search.query", HandleQueryAsync);
        bridge.Register("result.activate", HandleActivateAsync);
    }

    private async Task<object?> HandleQueryAsync(JsonElement? payload, CancellationToken _)
    {
        var request = payload?.Deserialize<SearchRequest>(BridgeJsonOptions.Default);
        var text = request?.Text ?? string.Empty;

        var token = BeginQuery();

        var current = settings.Current.Search;
        var query = new SearchQuery(
            text.Trim(),
            new HashSet<string>(current.EnabledCategories, StringComparer.OrdinalIgnoreCase),
            current.MaxResults);

        IReadOnlyList<SearchResult> latest = [];

        try
        {
            await foreach (var snapshot in engine.SearchAsync(query, token))
            {
                latest = snapshot;

                // Pushed as an event so the interface can paint before the slowest
                // provider has finished.
                bridge.PublishEvent("search.partial", new { query = query.Text, results = snapshot });
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer query. Returning what we have avoids leaving the
            // frontend's request unresolved, which would stall its loading indicator.
            return new { query = query.Text, results = latest };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search failed for '{Query}'.", query.Text);
            return new { query = query.Text, results = Array.Empty<SearchResult>() };
        }

        _lastResults = latest;
        _lastQueryText = query.Text;
        return new { query = query.Text, results = latest };
    }

    private CancellationToken BeginQuery()
    {
        lock (_queryLock)
        {
            // Cancel before replacing: the previous query's providers are still running
            // and must be told to stop.
            _currentQuery?.Cancel();
            _currentQuery?.Dispose();
            _currentQuery = new CancellationTokenSource();
            return _currentQuery.Token;
        }
    }

    private async Task<object?> HandleActivateAsync(JsonElement? payload, CancellationToken cancellationToken)
    {
        var request = payload?.Deserialize<ActivateRequest>(BridgeJsonOptions.Default);
        if (request is null || string.IsNullOrEmpty(request.ResultId))
        {
            return new { ok = false, message = "No result was specified." };
        }

        var result = _lastResults.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, request.ResultId, StringComparison.Ordinal));

        if (result is null)
        {
            return new { ok = false, message = "That result is no longer available." };
        }

        // Recorded before the action runs: what matters for ranking is that the user
        // chose it, not whether the target happened to launch successfully.
        frecency.RecordUse(result.Id);

        return result.Tag switch
        {
            InstalledApplication application => ActivateApplication(application, request.ActionId),
            string verb => await ActivateCommandAsync(verb, _lastQueryText, cancellationToken),
            _ => new { ok = false, message = "That result cannot be activated yet." },
        };
    }

    private object ActivateApplication(InstalledApplication application, string actionId)
    {
        var succeeded = actionId switch
        {
            "reveal" => launcher.RevealInExplorer(application.LaunchId),
            "copy-path" => true,
            _ => launcher.Launch(application),
        };

        return new
        {
            ok = succeeded,
            close = true,

            // Clipboard work happens in the frontend, which already has a focused
            // document and can use the async clipboard API without any extra permission.
            copyText = actionId == "copy-path" ? application.LaunchId : null,
            message = succeeded ? null : $"Could not open {application.Name}.",
        };
    }

    private async Task<object> ActivateCommandAsync(string verb, string queryText, CancellationToken cancellationToken)
    {
        // If the query already starts with this verb, run it verbatim so arguments
        // survive. Otherwise the command was found by fuzzy discovery rather than typed,
        // so run it bare and let it report that it needs arguments.
        var input = queryText.TrimStart().StartsWith(verb, StringComparison.OrdinalIgnoreCase)
            ? queryText.Trim()
            : verb;

        var outcome = await commands.ExecuteAsync(input, cancellationToken);

        if (outcome is null)
        {
            return new { ok = false, message = $"'{verb}' is not a known command." };
        }

        return new
        {
            ok = outcome.Succeeded,
            close = outcome.ShouldCloseLauncher,
            message = outcome.Message,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_queryLock)
        {
            _currentQuery?.Cancel();
            _currentQuery?.Dispose();
            _currentQuery = null;
        }
    }
}

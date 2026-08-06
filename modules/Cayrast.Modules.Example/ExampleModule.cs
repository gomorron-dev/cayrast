using Cayrast.Abstractions.Commands;
using Cayrast.Abstractions.Search;
using Cayrast.Abstractions.Settings;
using Cayrast.Sdk;

namespace Cayrast.Modules.Example;

/// <summary>
/// A worked example of a Cayrast module.
/// </summary>
/// <remarks>
/// <para>
/// Shows all three ways a module contributes to the launcher — a command, a search
/// provider, and a setting — using nothing but the public SDK. If you are writing your
/// first module, this file is the whole shape of the job.
/// </para>
/// <para>
/// It declares no permissions, which is the right default. Ask for a capability only
/// when you genuinely use it: every permission you request is one more reason for a
/// user to decline the install.
/// </para>
/// </remarks>
public sealed class ExampleModule : CayrastModule
{
    /// <inheritdoc />
    protected override Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        Log.Information("Example module starting.");

        // A command. The descriptor is data, so `help` picks it up automatically and
        // search can match against the verb, summary, and examples.
        Context.RegisterCommand(
            new CommandDescriptor
            {
                Verb = "reverse",
                Summary = "Reverse some text",
                Usage = "reverse <text>",
                Examples = ["reverse hello world"],

                // Safe to preview because it is pure and fast. Never set this on a
                // command that touches the system: preview runs on every keystroke.
                SupportsLivePreview = true,
            },
            new ReverseCommand());

        // A search provider, contributing results that rank alongside applications and
        // built-in commands through the same pipeline.
        Context.RegisterSearchProvider(new GreetingSearchProvider());

        // A setting, which appears on this module's settings page and becomes findable
        // through settings search without any extra work.
        Context.RegisterSetting(new SettingDescriptor
        {
            Id = "example.greeting",
            Category = "Example Module",
            Label = "Greeting",
            Description = "The word used by the example search provider.",
            Kind = SettingKind.Text,
            DefaultValue = "Hello",
            Keywords = ["greeting", "hello", "example", "salutation"],
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        Log.Information("Example module stopping.");
        return Task.CompletedTask;
    }
}

/// <summary>Reverses its argument.</summary>
internal sealed class ReverseCommand : ICommandHandler
{
    /// <inheritdoc />
    public ValueTask<string?> PreviewAsync(CommandInvocation invocation, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reverse(invocation.Arguments));

    /// <inheritdoc />
    public ValueTask<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken)
    {
        var reversed = Reverse(invocation.Arguments);

        // Expected failures are reported by returning Failure, not by throwing. A
        // thrown exception is treated as a bug in the command and logged as one.
        return ValueTask.FromResult(reversed is null
            ? CommandOutcome.Failure("Give some text to reverse.")
            : CommandOutcome.Display(reversed));
    }

    private static string? Reverse(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        // Reversed by text element rather than by char: reversing the UTF-16 code units
        // of "café" or an emoji produces mojibake, because a single visible character
        // can span several units.
        var elements = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(input);

        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current);
        }

        elements.Reverse();
        return string.Concat(elements);
    }
}

/// <summary>Contributes one result when the query mentions a greeting.</summary>
internal sealed class GreetingSearchProvider : SimpleSearchProvider
{
    /// <inheritdoc />
    public override string Id => "cayrast.example.greeting";

    /// <inheritdoc />
    public override SearchCategory Category => SearchCategory.Tools;

    /// <inheritdoc />
    /// <remarks>
    /// Narrowed deliberately. A provider that returns <see langword="true"/> for every
    /// query is scheduled on every keystroke; answering only for queries you can
    /// actually serve is the cheapest possible optimisation.
    /// </remarks>
    public override bool CanHandle(SearchQuery query) =>
        !query.IsEmpty && query.Text.Contains("hello", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override IEnumerable<SearchResult> GetResults(SearchQuery query)
    {
        yield return new SearchResult
        {
            // Stable across queries so frecency and deduplication can key on it.
            Id = "example:greeting",
            Title = "Hello from the example module",
            Subtitle = "Contributed by cayrast.example through the public SDK",
            Category = SearchCategory.Tools,
            Icon = IconReference.Glyph("sparkle"),
            Score = 0.9,
            Actions = [ResultAction.Default("Say hello")],
        };
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cayrast.Abstractions.Commands;

namespace Cayrast.Core.Commands;

/// <summary>
/// The commands Cayrast ships with.
/// </summary>
/// <remarks>
/// Registered through the same <see cref="ICommandEngine"/> API modules use. Nothing
/// here is privileged, which is the rule that keeps the public SDK honest: if a
/// built-in can do it, a third-party command can too.
/// </remarks>
public static class BuiltInCommands
{
    /// <summary>Registers every built-in command.</summary>
    public static void RegisterAll(ICommandEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        RegisterCalculator(engine);
        RegisterUuid(engine);
        RegisterBase64(engine);
        RegisterUrlEncoding(engine);
        RegisterHashing(engine);
        RegisterTimestamp(engine);
        RegisterJson(engine);
        RegisterHelp(engine);
        RegisterSettings(engine);
    }

    private static void RegisterCalculator(ICommandEngine engine) => engine.Register(
        new CommandDescriptor
        {
            Verb = "calc",
            Aliases = ["="],
            Summary = "Evaluate a mathematical expression",
            Usage = "calc <expression>",
            Examples = ["calc 20*50", "calc (1+2)^8", "calc sqrt(144)", "calc pi*2"],

            // Live preview is most of the value here: seeing the answer before pressing
            // Enter turns the launcher into a calculator rather than a way to open one.
            SupportsLivePreview = true,
        },
        new DelegateCommandHandler(
            preview: (invocation, _) =>
            {
                if (string.IsNullOrWhiteSpace(invocation.Arguments))
                {
                    return ValueTask.FromResult<string?>(null);
                }

                try
                {
                    return ValueTask.FromResult<string?>(ExpressionEvaluator.Format(ExpressionEvaluator.Evaluate(invocation.Arguments)));
                }
                catch (ExpressionException)
                {
                    // Half-typed input is the normal case on a per-keystroke preview:
                    // "2+" is not an error the user needs to be told about yet.
                    return ValueTask.FromResult<string?>(null);
                }
            },
            execute: (invocation, _) =>
            {
                try
                {
                    var value = ExpressionEvaluator.Format(ExpressionEvaluator.Evaluate(invocation.Arguments));
                    return ValueTask.FromResult(CommandOutcome.Display(value));
                }
                catch (ExpressionException ex)
                {
                    return ValueTask.FromResult(CommandOutcome.Failure(ex.Message));
                }
            }));

    private static void RegisterUuid(ICommandEngine engine) => engine.Register(
        new CommandDescriptor
        {
            Verb = "uuid",
            Aliases = ["guid"],
            Summary = "Generate a random UUID",
            Usage = "uuid [count]",
            Examples = ["uuid", "uuid 5"],
        },
        new DelegateCommandHandler(execute: (invocation, _) =>
        {
            var count = 1;

            if (!string.IsNullOrWhiteSpace(invocation.Arguments)
                && (!int.TryParse(invocation.Arguments, CultureInfo.InvariantCulture, out count) || count < 1))
            {
                return ValueTask.FromResult(CommandOutcome.Failure("Give a positive number of UUIDs to generate."));
            }

            // Capped so a mistyped argument cannot build a hundred-megabyte string and
            // stall the interface.
            count = Math.Min(count, 100);

            var values = string.Join(
                Environment.NewLine,
                Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString("D")));

            return ValueTask.FromResult(CommandOutcome.Display(values));
        }));

    private static void RegisterBase64(ICommandEngine engine)
    {
        engine.Register(
            new CommandDescriptor
            {
                Verb = "base64",
                Aliases = ["b64"],
                Summary = "Encode text as Base64",
                Usage = "base64 <text>",
                Examples = ["base64 hello world"],
                SupportsLivePreview = true,
            },
            new DelegateCommandHandler(
                preview: (invocation, _) => ValueTask.FromResult<string?>(
                    string.IsNullOrEmpty(invocation.Arguments)
                        ? null
                        : Convert.ToBase64String(Encoding.UTF8.GetBytes(invocation.Arguments))),
                execute: (invocation, _) => ValueTask.FromResult(
                    string.IsNullOrEmpty(invocation.Arguments)
                        ? CommandOutcome.Failure("Give some text to encode.")
                        : CommandOutcome.Display(Convert.ToBase64String(Encoding.UTF8.GetBytes(invocation.Arguments))))));

        engine.Register(
            new CommandDescriptor
            {
                Verb = "unbase64",
                Aliases = ["b64d"],
                Summary = "Decode Base64 back to text",
                Usage = "unbase64 <base64>",
                Examples = ["unbase64 aGVsbG8gd29ybGQ="],
                SupportsLivePreview = true,
            },
            new DelegateCommandHandler(
                preview: (invocation, _) => ValueTask.FromResult(TryDecodeBase64(invocation.Arguments)),
                execute: (invocation, _) =>
                {
                    var decoded = TryDecodeBase64(invocation.Arguments);
                    return ValueTask.FromResult(decoded is null
                        ? CommandOutcome.Failure("That is not valid Base64.")
                        : CommandOutcome.Display(decoded));
                }));
    }

    private static string? TryDecodeBase64(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        Span<byte> buffer = new byte[input.Length];
        if (!Convert.TryFromBase64String(input, buffer, out var written))
        {
            return null;
        }

        try
        {
            // Base64 can decode to arbitrary bytes that are not valid UTF-8. Strict
            // decoding turns that into a clean failure rather than a string of
            // replacement characters that looks like a successful decode.
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer[..written]);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static void RegisterUrlEncoding(ICommandEngine engine)
    {
        engine.Register(
            new CommandDescriptor
            {
                Verb = "urlencode",
                Summary = "Percent-encode text for use in a URL",
                Usage = "urlencode <text>",
                Examples = ["urlencode hello world&more"],
                SupportsLivePreview = true,
            },
            new DelegateCommandHandler(
                preview: (invocation, _) => ValueTask.FromResult<string?>(
                    string.IsNullOrEmpty(invocation.Arguments) ? null : Uri.EscapeDataString(invocation.Arguments)),
                execute: (invocation, _) => ValueTask.FromResult(
                    CommandOutcome.Display(Uri.EscapeDataString(invocation.Arguments)))));

        engine.Register(
            new CommandDescriptor
            {
                Verb = "urldecode",
                Summary = "Decode a percent-encoded URL",
                Usage = "urldecode <text>",
                Examples = ["urldecode hello%20world"],
                SupportsLivePreview = true,
            },
            new DelegateCommandHandler(
                preview: (invocation, _) =>
                {
                    try
                    {
                        return ValueTask.FromResult<string?>(
                            string.IsNullOrEmpty(invocation.Arguments) ? null : Uri.UnescapeDataString(invocation.Arguments));
                    }
                    catch (UriFormatException)
                    {
                        return ValueTask.FromResult<string?>(null);
                    }
                },
                execute: (invocation, _) =>
                {
                    try
                    {
                        return ValueTask.FromResult(CommandOutcome.Display(Uri.UnescapeDataString(invocation.Arguments)));
                    }
                    catch (UriFormatException)
                    {
                        return ValueTask.FromResult(CommandOutcome.Failure("That is not a valid percent-encoded string."));
                    }
                }));
    }

    private static void RegisterHashing(ICommandEngine engine)
    {
        foreach (var (verb, algorithm) in new (string Verb, string Algorithm)[]
                 {
                     ("md5", "MD5"), ("sha1", "SHA1"), ("sha256", "SHA256"), ("sha512", "SHA512"),
                 })
        {
            var captured = algorithm;

            engine.Register(
                new CommandDescriptor
                {
                    Verb = verb,
                    Summary = $"Compute the {algorithm} hash of some text",
                    Usage = $"{verb} <text>",
                    Examples = [$"{verb} hello"],
                    SupportsLivePreview = true,
                },
                new DelegateCommandHandler(
                    preview: (invocation, _) => ValueTask.FromResult<string?>(
                        string.IsNullOrEmpty(invocation.Arguments) ? null : ComputeHash(captured, invocation.Arguments)),
                    execute: (invocation, _) => ValueTask.FromResult(
                        string.IsNullOrEmpty(invocation.Arguments)
                            ? CommandOutcome.Failure("Give some text to hash.")
                            : CommandOutcome.Display(ComputeHash(captured, invocation.Arguments)))));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security", "CA5351:Do not use broken cryptographic algorithms",
        Justification = "Offered as a checksum utility, not for any security decision. Developers " +
                        "routinely need MD5 to compare against a published checksum, and refusing to " +
                        "provide it would not make those checksums stronger — it would only send the " +
                        "user to a website that pastes their data into someone else's server.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security", "CA5350:Do not use weak cryptographic algorithms",
        Justification = "As above: SHA1 is required to verify git object hashes and legacy checksums. " +
                        "Cayrast never uses these algorithms to make a trust decision of its own.")]
    private static string ComputeHash(string algorithm, string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);

        // MD5 and SHA1 are broken for security but remain the correct answer for
        // checksum comparison against tools that emit them, which is what a developer
        // utility is for. They are not offered anywhere Cayrast makes a trust decision.
        var hash = algorithm switch
        {
            "MD5" => MD5.HashData(bytes),
            "SHA1" => SHA1.HashData(bytes),
            "SHA256" => SHA256.HashData(bytes),
            "SHA512" => SHA512.HashData(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

        return Convert.ToHexStringLower(hash);
    }

    private static void RegisterTimestamp(ICommandEngine engine) => engine.Register(
        new CommandDescriptor
        {
            Verb = "timestamp",
            Aliases = ["ts", "unix"],
            Summary = "Convert between Unix timestamps and dates",
            Usage = "timestamp [value]",
            Examples = ["timestamp", "timestamp 1700000000", "timestamp 2024-01-15"],
            SupportsLivePreview = true,
        },
        new DelegateCommandHandler(
            preview: (invocation, _) => ValueTask.FromResult(ConvertTimestamp(invocation.Arguments)),
            execute: (invocation, _) =>
            {
                var converted = ConvertTimestamp(invocation.Arguments);
                return ValueTask.FromResult(converted is null
                    ? CommandOutcome.Failure("Give a Unix timestamp or a date.")
                    : CommandOutcome.Display(converted));
            }));

    private static string? ConvertTimestamp(string input)
    {
        // No argument means "what time is it", which is the most common reason to
        // reach for this command at all.
        if (string.IsNullOrWhiteSpace(input))
        {
            var now = DateTimeOffset.UtcNow;
            return $"{now.ToUnixTimeSeconds()}  ·  {now:yyyy-MM-dd HH:mm:ss} UTC";
        }

        if (long.TryParse(input, CultureInfo.InvariantCulture, out var epoch))
        {
            // Values past ~2001 in milliseconds exceed any plausible second-based
            // timestamp, so the unit can be inferred rather than asked for.
            var isMilliseconds = Math.Abs(epoch) > 100_000_000_000L;

            try
            {
                var instant = isMilliseconds
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                    : DateTimeOffset.FromUnixTimeSeconds(epoch);

                return $"{instant:yyyy-MM-dd HH:mm:ss} UTC  ·  {instant.ToLocalTime():yyyy-MM-dd HH:mm:ss} local";
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return $"{parsed.ToUnixTimeSeconds()}  ·  {parsed.ToUnixTimeMilliseconds()} ms";
        }

        return null;
    }

    private static void RegisterJson(ICommandEngine engine) => engine.Register(
        new CommandDescriptor
        {
            Verb = "json",
            Summary = "Format and validate JSON",
            Usage = "json <json>",
            Examples = ["""json {"a":1,"b":[2,3]}"""],
        },
        new DelegateCommandHandler(execute: (invocation, _) =>
        {
            if (string.IsNullOrWhiteSpace(invocation.Arguments))
            {
                return ValueTask.FromResult(CommandOutcome.Failure("Give some JSON to format."));
            }

            try
            {
                using var document = JsonDocument.Parse(invocation.Arguments);
                var formatted = JsonSerializer.Serialize(document.RootElement, JsonFormatting);
                return ValueTask.FromResult(CommandOutcome.Display(formatted));
            }
            catch (JsonException ex)
            {
                // The parser's message names the line and position, which is the whole
                // point of running a validator.
                return ValueTask.FromResult(CommandOutcome.Failure($"Invalid JSON: {ex.Message}"));
            }
        }));

    private static readonly JsonSerializerOptions JsonFormatting = new() { WriteIndented = true };

    private static void RegisterHelp(ICommandEngine engine) => engine.Register(
        new CommandDescriptor
        {
            Verb = "help",
            Aliases = ["?"],
            Summary = "List every available command",
            Usage = "help [command]",
            Examples = ["help", "help calc"],
        },
        new DelegateCommandHandler(execute: (invocation, _) =>
        {
            // Built from the live descriptor registry rather than a hand-written list,
            // so a module's commands appear here the moment it loads and nothing can
            // drift out of date.
            var commands = engine.Commands;

            if (!string.IsNullOrWhiteSpace(invocation.Arguments))
            {
                var match = commands.FirstOrDefault(descriptor =>
                    string.Equals(descriptor.Verb, invocation.Arguments, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    return ValueTask.FromResult(CommandOutcome.Failure($"No command named '{invocation.Arguments}'."));
                }

                var detail = new StringBuilder()
                    .AppendLine(CultureInfo.InvariantCulture, $"{match.Verb} — {match.Summary}");

                if (!string.IsNullOrEmpty(match.Usage))
                {
                    detail.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Usage: {match.Usage}");
                }

                if (match.Aliases.Count > 0)
                {
                    detail.AppendLine(CultureInfo.InvariantCulture, $"Aliases: {string.Join(", ", match.Aliases)}");
                }

                if (match.Examples.Count > 0)
                {
                    detail.AppendLine().AppendLine("Examples:");
                    foreach (var example in match.Examples)
                    {
                        detail.AppendLine(CultureInfo.InvariantCulture, $"  {example}");
                    }
                }

                return ValueTask.FromResult(CommandOutcome.Display(detail.ToString().TrimEnd()));
            }

            var listing = new StringBuilder();
            foreach (var descriptor in commands)
            {
                listing.AppendLine(CultureInfo.InvariantCulture, $"{descriptor.Verb,-12} {descriptor.Summary}");
            }

            listing.AppendLine().Append("Type 'help <command>' for details and examples.");
            return ValueTask.FromResult(CommandOutcome.Display(listing.ToString()));
        }));

    private static void RegisterSettings(ICommandEngine engine) => engine.Register(
        new CommandDescriptor
        {
            Verb = "settings",
            Aliases = ["config", "options", "preferences", "configure"],
            Summary = "Open Cayrast application settings",
            Usage = "settings",
            Examples = ["settings", "config"],
        },
        new DelegateCommandHandler(execute: (_, _) =>
            ValueTask.FromResult(CommandOutcome.OpenView("settings"))));
}

/// <summary>Adapts delegates to <see cref="ICommandHandler"/>.</summary>
/// <remarks>
/// Most commands are a single expression. A class per command would be ceremony that
/// makes the set harder to read as a whole, and the whole set is what
/// <see cref="BuiltInCommands"/> exists to present.
/// </remarks>
/// <remarks>
/// Deliberately a single constructor. An overload with the parameters reversed would
/// read more naturally at some call sites, but named arguments then become ambiguous
/// because both delegates are structurally similar. Call sites pass
/// <c>preview:</c> and <c>execute:</c> by name, so order does not matter anyway.
/// </remarks>
internal sealed class DelegateCommandHandler(
    Func<CommandInvocation, CancellationToken, ValueTask<CommandOutcome>> execute,
    Func<CommandInvocation, CancellationToken, ValueTask<string?>>? preview = null) : ICommandHandler
{
    /// <inheritdoc />
    public ValueTask<string?> PreviewAsync(CommandInvocation invocation, CancellationToken cancellationToken) =>
        preview?.Invoke(invocation, cancellationToken) ?? ValueTask.FromResult<string?>(null);

    /// <inheritdoc />
    public ValueTask<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken) =>
        execute(invocation, cancellationToken);
}

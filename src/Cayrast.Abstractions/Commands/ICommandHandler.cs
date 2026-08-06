namespace Cayrast.Abstractions.Commands;

/// <summary>Executes a registered command.</summary>
public interface ICommandHandler
{
    /// <summary>
    /// Computes the inline preview shown while the user is still typing.
    /// </summary>
    /// <remarks>
    /// Called on every keystroke, so it must be fast and free of side effects. Only
    /// invoked when the descriptor sets
    /// <see cref="CommandDescriptor.SupportsLivePreview"/>; the default returns nothing.
    /// </remarks>
    ValueTask<string?> PreviewAsync(CommandInvocation invocation, CancellationToken cancellationToken)
        => ValueTask.FromResult<string?>(null);

    /// <summary>Runs the command after the user commits it.</summary>
    ValueTask<CommandOutcome> ExecuteAsync(CommandInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>One invocation of a command, already split into verb and arguments.</summary>
/// <param name="Verb">The matched verb or alias, as typed.</param>
/// <param name="Arguments">Everything after the verb, trimmed. Empty when no arguments were given.</param>
public sealed record CommandInvocation(string Verb, string Arguments);

/// <summary>The result of running a command.</summary>
/// <remarks>
/// Commands report failure by returning <see cref="Failure"/>, not by throwing.
/// A thrown exception is treated as a bug in the command and logged as such; an
/// expected failure — malformed input, a missing file — is a normal outcome the user
/// needs to see, and dressing it up as a crash report helps nobody.
/// </remarks>
/// <param name="Succeeded">Whether the command did what was asked.</param>
/// <param name="Message">Text shown to the user. On failure, this must explain what went wrong.</param>
/// <param name="ShouldCloseLauncher">
/// Whether to dismiss the window. False for commands whose output the user needs to
/// read, such as <c>help</c> or <c>calc</c>.
/// </param>
public sealed record CommandOutcome(bool Succeeded, string? Message = null, bool ShouldCloseLauncher = true)
{
    /// <summary>The command ran and the launcher should dismiss.</summary>
    public static CommandOutcome Ok(string? message = null) => new(true, message);

    /// <summary>The command produced output the user needs to read, so the launcher stays open.</summary>
    public static CommandOutcome Display(string message) => new(true, message, ShouldCloseLauncher: false);

    /// <summary>The command failed for an expected reason. The launcher stays open showing why.</summary>
    public static CommandOutcome Failure(string message) => new(false, message, ShouldCloseLauncher: false);
}

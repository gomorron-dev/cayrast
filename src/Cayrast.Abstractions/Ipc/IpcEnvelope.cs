using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cayrast.Abstractions.Ipc;

/// <summary>What kind of traffic an <see cref="IpcEnvelope"/> carries.</summary>
public enum IpcMessageKind
{
    /// <summary>Expects exactly one <see cref="Response"/> or <see cref="Fault"/> in reply.</summary>
    Request = 0,

    /// <summary>Successful reply to a request with a matching correlation id.</summary>
    Response = 1,

    /// <summary>Fire-and-forget notification. No reply is sent and none is awaited.</summary>
    Event = 2,

    /// <summary>Failed reply to a request. Carries <see cref="IpcEnvelope.Fault"/>.</summary>
    Fault = 3,
}

/// <summary>
/// The single wire format for all host-to-module communication.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why everything goes through one envelope.</b> This is the keystone of the
/// hybrid hosting model. Because both hosting paths speak this exact contract, an
/// in-process module and a sandboxed one are indistinguishable from the module's
/// side, so a module's trust level becomes a setting the user can change rather than
/// a decision baked into its source. Without this, "run untrusted plugins safely"
/// would mean two parallel APIs and a rewrite for every module that switched.
/// </para>
/// <para>
/// <b>On serialisation cost.</b> The in-process path currently pays for JSON
/// round-tripping it does not strictly need. That is a deliberate trade: one code
/// path is far easier to reason about and test than two, and the cost is measured in
/// microseconds against a UI budget measured in milliseconds. If profiling later
/// shows it matters, the in-process transport can bypass serialisation behind this
/// same type — the contract is what must stay fixed, not the encoding.
/// </para>
/// </remarks>
public sealed record IpcEnvelope
{
    /// <summary>Message classification.</summary>
    [JsonPropertyName("kind")]
    public required IpcMessageKind Kind { get; init; }

    /// <summary>
    /// Ties a response or fault back to its request. Unique per in-flight request.
    /// </summary>
    [JsonPropertyName("id")]
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Dotted operation name, e.g. <c>search.query</c> or <c>clipboard.read</c>.
    /// </summary>
    /// <remarks>
    /// The permission broker dispatches on this. Channel names are part of the public
    /// contract and cannot be renamed without breaking published modules.
    /// </remarks>
    [JsonPropertyName("channel")]
    public required string Channel { get; init; }

    /// <summary>
    /// Module the message originated from, or <see langword="null"/> when it came from the host.
    /// </summary>
    /// <remarks>
    /// Assigned by the transport from the authenticated pipe connection, never read
    /// from the message body — a sandboxed module must not be able to impersonate
    /// another module by setting a field.
    /// </remarks>
    [JsonPropertyName("origin")]
    public string? Origin { get; init; }

    /// <summary>Operation-specific payload, shaped by <see cref="Channel"/>.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>Error detail. Present only when <see cref="Kind"/> is <see cref="IpcMessageKind.Fault"/>.</summary>
    [JsonPropertyName("fault")]
    public IpcFault? Fault { get; init; }
}

/// <summary>Why a request failed.</summary>
/// <param name="Code">Machine-readable reason, for programmatic handling.</param>
/// <param name="Message">
/// Human-readable explanation. Safe to show the user, so it must not leak host
/// internals such as full paths or stack traces to a sandboxed module.
/// </param>
public sealed record IpcFault(IpcFaultCode Code, string Message);

/// <summary>Machine-readable failure reasons.</summary>
public enum IpcFaultCode
{
    /// <summary>Cause did not fit any other code. Details are in the message.</summary>
    Unknown = 0,

    /// <summary>The caller lacks the permission this channel requires. Not retryable.</summary>
    PermissionDenied = 1,

    /// <summary>No handler is registered for the channel — usually a host/module version mismatch.</summary>
    UnknownChannel = 2,

    /// <summary>The payload was missing, malformed, or failed validation.</summary>
    InvalidPayload = 3,

    /// <summary>The handler exceeded its time budget. May be retryable.</summary>
    Timeout = 4,

    /// <summary>The module process died or was killed while the request was in flight.</summary>
    ModuleUnavailable = 5,

    /// <summary>The operation was cancelled, typically by a newer keystroke superseding it.</summary>
    Cancelled = 6,
}

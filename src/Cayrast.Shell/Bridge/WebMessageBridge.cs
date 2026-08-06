using System.Text.Json;
using Cayrast.Abstractions.Ipc;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Cayrast.Shell.Bridge;

/// <summary>Handles one bridge channel.</summary>
/// <param name="payload">Request payload, or <see langword="null"/> if none was sent.</param>
/// <param name="cancellationToken">Cancelled when the host shuts down.</param>
/// <returns>A value to serialise as the response payload, or <see langword="null"/>.</returns>
public delegate Task<object?> BridgeHandler(JsonElement? payload, CancellationToken cancellationToken);

/// <summary>
/// The typed message channel between the WebView2 frontend and the host.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately reuses <see cref="IpcEnvelope"/> — the same envelope modules speak
/// across the sandbox boundary. One wire format means the frontend, an in-process
/// module, and a sandboxed module are all talking to the host in the same shape, and
/// only one protocol has to be documented, versioned, and debugged.
/// </para>
/// <para>
/// <b>The frontend is not trusted.</b> It runs arbitrary rendering code and hosts
/// third-party module UIs in iframes. Every payload is validated here, and no handler
/// may assume well-formed input.
/// </para>
/// </remarks>
public sealed class WebMessageBridge(ILogger<WebMessageBridge> logger)
{
    private readonly Dictionary<string, BridgeHandler> _handlers = new(StringComparer.Ordinal);
    private CoreWebView2? _webView;

    /// <summary>Registers a handler for a channel. Replaces any existing handler.</summary>
    public void Register(string channel, BridgeHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[channel] = handler;
    }

    /// <summary>Begins listening for messages from the given WebView2 instance.</summary>
    public void Attach(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        _webView = webView;
        webView.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>Pushes an unsolicited event to the frontend.</summary>
    /// <remarks>
    /// Used for host-originated changes the UI must react to, such as a settings
    /// change made from the tray or a theme following the system switching to dark.
    /// </remarks>
    public void PublishEvent(string channel, object? payload)
    {
        if (_webView is null)
        {
            return;
        }

        var envelope = new IpcEnvelope
        {
            Kind = IpcMessageKind.Event,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Channel = channel,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, BridgeJsonOptions.Default),
        };

        Post(envelope);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        IpcEnvelope? request = null;

        try
        {
            // WebMessageAsJson is what postMessage sent. Anything can arrive here,
            // including malformed JSON from a misbehaving module UI.
            request = JsonSerializer.Deserialize<IpcEnvelope>(e.WebMessageAsJson, BridgeJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Discarded a malformed bridge message.");
            return;
        }

        if (request is null || string.IsNullOrEmpty(request.Channel))
        {
            logger.LogWarning("Discarded a bridge message with no channel.");
            return;
        }

        if (!_handlers.TryGetValue(request.Channel, out var handler))
        {
            logger.LogWarning("No handler registered for bridge channel '{Channel}'.", request.Channel);
            Respond(request, IpcFaultCode.UnknownChannel, $"Unknown channel '{request.Channel}'.");
            return;
        }

        try
        {
            var result = await handler(request.Payload, CancellationToken.None);

            // Events are fire-and-forget; replying to one would leave the frontend
            // holding a response nothing is waiting for.
            if (request.Kind == IpcMessageKind.Request)
            {
                Post(new IpcEnvelope
                {
                    Kind = IpcMessageKind.Response,
                    CorrelationId = request.CorrelationId,
                    Channel = request.Channel,
                    Payload = result is null ? null : JsonSerializer.SerializeToElement(result, BridgeJsonOptions.Default),
                });
            }
        }
        catch (Exception ex)
        {
            // An async void event handler is the one place an escaping exception
            // terminates the process, so nothing may propagate past here.
            logger.LogError(ex, "Bridge handler for '{Channel}' threw.", request.Channel);

            // The message is deliberately generic: a sandboxed module UI must not
            // learn host paths or type names from an exception.
            Respond(request, IpcFaultCode.Unknown, "The operation failed. See the Cayrast log for details.");
        }
    }

    private void Respond(IpcEnvelope request, IpcFaultCode code, string message)
    {
        if (request.Kind != IpcMessageKind.Request)
        {
            return;
        }

        Post(new IpcEnvelope
        {
            Kind = IpcMessageKind.Fault,
            CorrelationId = request.CorrelationId,
            Channel = request.Channel,
            Fault = new IpcFault(code, message),
        });
    }

    private void Post(IpcEnvelope envelope)
    {
        if (_webView is null)
        {
            return;
        }

        try
        {
            _webView.PostWebMessageAsJson(JsonSerializer.Serialize(envelope, BridgeJsonOptions.Default));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The WebView can be torn down between a request arriving and its
            // response being posted, for example during shutdown.
            logger.LogDebug("Dropped a bridge message: the WebView is no longer available.");
        }
    }
}

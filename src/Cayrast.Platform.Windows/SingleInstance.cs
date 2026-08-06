using Cayrast.Abstractions;

namespace Cayrast.Platform.Windows;

/// <summary>
/// Ensures only one Cayrast runs per user session, and lets a second launch wake the
/// first rather than failing silently.
/// </summary>
/// <remarks>
/// <para>
/// A second instance would register a duplicate tray icon, fight over the global
/// hotkey, and race the first on the settings file. But simply exiting would be a poor
/// experience: someone who clicks the Start Menu shortcut while Cayrast is already
/// resident in the tray expects the launcher to appear, not for nothing to happen.
/// </para>
/// <para>
/// So detection uses a named mutex and activation uses a named auto-reset event: the
/// second instance sets the event and exits, and the primary wakes and shows itself.
/// </para>
/// <para>
/// <b>Why not a broadcast window message.</b> The obvious approach — posting a
/// registered message to <c>HWND_BROADCAST</c> — cannot work here. Broadcasts are
/// delivered only to top-level windows, and Cayrast's message window is deliberately
/// message-only (parented to <c>HWND_MESSAGE</c>) so it stays out of the shell
/// entirely. Message-only windows never receive broadcasts, so the signal would be
/// dropped with no error anywhere. A named event has no such asymmetry, needs no
/// window at all, and works before the UI exists.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = $@"Local\{CayrastBrand.SlugName}-single-instance";
    private const string ActivationEventName = $@"Local\{CayrastBrand.SlugName}-activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle? activationEvent, bool isPrimary)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        IsPrimary = isPrimary;
    }

    /// <summary>Whether this process owns the instance lock and should continue starting.</summary>
    public bool IsPrimary { get; }

    /// <summary>Attempts to claim the single-instance lock.</summary>
    /// <remarks>
    /// The handles are session-local (<c>Local\</c>) rather than global, so separate
    /// users on the same machine each get their own Cayrast — which is correct, since
    /// settings and data are per-user.
    /// </remarks>
    public static SingleInstance Acquire()
    {
        // Requesting initial ownership makes the check and the claim one atomic step,
        // closing the race between two simultaneous launches.
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        // Only the primary owns the event; a secondary opens it briefly to signal.
        var activationEvent = createdNew
            ? new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName)
            : null;

        return new SingleInstance(mutex, activationEvent, createdNew);
    }

    /// <summary>
    /// Registers the callback invoked when another launch requests activation.
    /// </summary>
    /// <param name="callback">
    /// Runs on a thread-pool thread, so anything touching the UI must marshal to the
    /// dispatcher itself.
    /// </param>
    /// <remarks>Does nothing on a non-primary instance.</remarks>
    public void OnActivationRequested(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!IsPrimary || _activationEvent is null)
        {
            return;
        }

        // A thread-pool wait rather than a dedicated thread: this fires a handful of
        // times in a session at most, and a whole thread parked on it would be waste.
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    callback();
                }
            },
            state: null,
            Timeout.Infinite,

            // The event must keep firing for every subsequent launch, not just the first.
            executeOnlyOnce: false);
    }

    /// <summary>Asks an already-running instance to show its launcher.</summary>
    /// <returns><see langword="false"/> if no running instance was found.</returns>
    public static bool SignalExistingInstance()
    {
        // The primary can exit between our mutex check and this call, so a missing
        // event is a normal race rather than an error.
        if (!EventWaitHandle.TryOpenExisting(ActivationEventName, out var handle))
        {
            return false;
        }

        using (handle)
        {
            return handle.Set();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();

        if (IsPrimary)
        {
            // Release before disposing so the next launch can claim it immediately
            // instead of hitting an abandoned-mutex exception.
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned — already released, or ownership was never taken.
            }
        }

        _mutex.Dispose();
    }
}

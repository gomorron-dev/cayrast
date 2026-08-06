using System.Runtime.InteropServices;
using Cayrast.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows;

/// <summary>
/// Binds this process and everything it spawns into a Windows job object that is
/// destroyed when Cayrast exits — however it exits.
/// </summary>
/// <remarks>
/// <para>
/// WebView2 runs the browser out of process, spawning half a dozen
/// <c>msedgewebview2.exe</c> children. On a clean shutdown it reaps them itself. On a
/// crash, a force-kill, or a debugger stop it does not, and they are left running.
/// </para>
/// <para>
/// For a launcher that stays resident all day and may be restarted repeatedly during
/// development or after an update, those orphans accumulate — each holding tens of
/// megabytes — until the user notices unexplained memory use and has no idea what to
/// blame. A job object with <c>KILL_ON_JOB_CLOSE</c> makes the kernel guarantee the
/// cleanup: when the last handle to the job closes, which happens automatically when
/// our process dies for any reason, every process in it is terminated.
/// </para>
/// <para>
/// Must be created before WebView2 initialises so the children inherit membership.
/// </para>
/// </remarks>
public sealed class ChildProcessJob : IDisposable
{
    private readonly ILogger<ChildProcessJob> _logger;
    private nint _jobHandle;
    private bool _disposed;

    /// <summary>Creates the job and assigns the current process to it.</summary>
    /// <remarks>
    /// Failure is logged and tolerated. Job objects can be unavailable under some
    /// container and sandbox configurations, and orphaned browser processes are a
    /// tidiness problem — not a reason to refuse to start.
    /// </remarks>
    public ChildProcessJob(ILogger<ChildProcessJob> logger)
    {
        _logger = logger;

        _jobHandle = NativeMethods.CreateJobObject(0, null);
        if (_jobHandle == 0)
        {
            _logger.LogWarning(
                "Could not create the child-process job (Win32 error {Error}). WebView2 processes may outlive a crash.",
                Marshal.GetLastWin32Error());
            return;
        }

        if (!ConfigureKillOnClose() || !AssignCurrentProcess())
        {
            NativeMethods.CloseHandle(_jobHandle);
            _jobHandle = 0;
            return;
        }

        _logger.LogDebug("Child-process job created; WebView2 processes will not outlive this one.");
    }

    private unsafe bool ConfigureKillOnClose()
    {
        var information = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var size = (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
        if (NativeMethods.SetInformationJobObject(
                _jobHandle, NativeMethods.JobObjectExtendedLimitInformation, (nint)(&information), size))
        {
            return true;
        }

        _logger.LogWarning(
            "Could not configure the child-process job (Win32 error {Error}).",
            Marshal.GetLastWin32Error());
        return false;
    }

    private bool AssignCurrentProcess()
    {
        if (NativeMethods.AssignProcessToJobObject(_jobHandle, NativeMethods.GetCurrentProcess()))
        {
            return true;
        }

        // Expected when an outer job already governs this process and forbids nesting,
        // which is common under some CI runners and debugging hosts.
        _logger.LogDebug(
            "Could not join the child-process job (Win32 error {Error}); an outer job may already own this process.",
            Marshal.GetLastWin32Error());
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_jobHandle != 0)
        {
            // Closing the last handle triggers KILL_ON_JOB_CLOSE, terminating any
            // remaining WebView2 children.
            NativeMethods.CloseHandle(_jobHandle);
            _jobHandle = 0;
        }
    }
}

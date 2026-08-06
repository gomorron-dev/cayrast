using System.Collections.Concurrent;
using Cayrast.Abstractions.Modules;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Modules;

/// <summary>One recorded use of a capability by a module.</summary>
/// <param name="ModuleId">Which module acted.</param>
/// <param name="Permission">Which capability it used.</param>
/// <param name="Detail">What it touched, e.g. a path or a host name.</param>
/// <param name="Allowed">Whether the request was permitted.</param>
/// <param name="TimestampUtc">When it happened.</param>
public sealed record ModuleActivity(
    ModuleId ModuleId,
    ModulePermission Permission,
    string Detail,
    bool Allowed,
    DateTime TimestampUtc);

/// <summary>Raised when a module attempts something it was not granted.</summary>
public sealed class PermissionDeniedException(ModuleId moduleId, ModulePermission permission)
    : Exception($"Module '{moduleId}' does not have the '{permission}' permission.")
{
    /// <summary>The module that was refused.</summary>
    public ModuleId ModuleId { get; } = moduleId;

    /// <summary>The capability it lacked.</summary>
    public ModulePermission Permission { get; } = permission;
}

/// <summary>
/// The single point where a module's declared permissions are checked.
/// </summary>
/// <remarks>
/// <para>
/// Modules never receive a raw <see cref="FileStream"/> or <see cref="HttpClient"/>.
/// They ask the host to act, and the host checks the granted set first. That is what
/// makes the permission list enforcement rather than documentation — and it gives the
/// "view module activity" feature a natural place to record from, because every
/// capability use necessarily passes through here.
/// </para>
/// <para>
/// <b>⚠️ What this does and does not guarantee, today.</b> Every module currently loads
/// in-process, because <c>Cayrast.ModuleHost</c> is still a stub. In-process, this check
/// is <em>advisory</em>: a loaded assembly can P/Invoke anything regardless of what it
/// declared, so the broker catches mistakes and honest modules degrading gracefully —
/// not malice.
/// </para>
/// <para>
/// Once the sandbox exists, the same check becomes enforcement for third-party modules,
/// because it will be backed by an operating system boundary: a low-integrity process
/// that cannot reach the filesystem by calling Win32 directly. Nothing in this class
/// changes when that happens; only where the module runs does.
/// </para>
/// </remarks>
public sealed class PermissionBroker(ILogger<PermissionBroker> logger)
{
    /// <summary>How many activity records to keep per module.</summary>
    /// <remarks>
    /// Bounded because this is a diagnostic surface, not an audit log. A module polling
    /// a file would otherwise grow it without limit for the life of the session.
    /// </remarks>
    private const int MaxActivityPerModule = 500;

    private readonly ConcurrentDictionary<ModuleId, ModulePermission> _granted = [];
    private readonly ConcurrentDictionary<ModuleId, ConcurrentQueue<ModuleActivity>> _activity = [];

    /// <summary>Records what the user granted a module.</summary>
    public void Grant(ModuleId moduleId, ModulePermission permissions)
    {
        _granted[moduleId] = permissions;
        logger.LogInformation("Module '{Module}' granted: {Permissions}.", moduleId, permissions);
    }

    /// <summary>Removes a module's grants, e.g. when it is disabled or uninstalled.</summary>
    public void Revoke(ModuleId moduleId)
    {
        _granted.TryRemove(moduleId, out _);
        _activity.TryRemove(moduleId, out _);
    }

    /// <summary>What a module was actually granted, which may be less than it requested.</summary>
    public ModulePermission GetGranted(ModuleId moduleId) =>
        _granted.TryGetValue(moduleId, out var permissions) ? permissions : ModulePermission.None;

    /// <summary>Whether a module holds a capability.</summary>
    public bool IsGranted(ModuleId moduleId, ModulePermission permission) =>
        (GetGranted(moduleId) & permission) == permission;

    /// <summary>
    /// Checks a capability, records the attempt, and throws if it was not granted.
    /// </summary>
    /// <param name="moduleId">The calling module.</param>
    /// <param name="permission">The capability required.</param>
    /// <param name="detail">What is being touched, for the activity record.</param>
    /// <exception cref="PermissionDeniedException">The module lacks the capability.</exception>
    /// <remarks>
    /// Throws rather than returning a failure code, and denials are recorded as well as
    /// grants. Silently no-oping would teach module authors that the permission system
    /// can be ignored, and would hide a module repeatedly probing for capabilities it
    /// was refused — which is exactly what the activity view exists to surface.
    /// </remarks>
    public void Demand(ModuleId moduleId, ModulePermission permission, string detail)
    {
        var allowed = IsGranted(moduleId, permission);
        Record(moduleId, permission, detail, allowed);

        if (allowed)
        {
            return;
        }

        logger.LogWarning(
            "Module '{Module}' attempted '{Permission}' without permission ({Detail}).",
            moduleId, permission, detail);

        throw new PermissionDeniedException(moduleId, permission);
    }

    /// <summary>Everything a module has done, oldest first.</summary>
    public IReadOnlyList<ModuleActivity> GetActivity(ModuleId moduleId) =>
        _activity.TryGetValue(moduleId, out var queue) ? [.. queue] : [];

    private void Record(ModuleId moduleId, ModulePermission permission, string detail, bool allowed)
    {
        var queue = _activity.GetOrAdd(moduleId, _ => new ConcurrentQueue<ModuleActivity>());
        queue.Enqueue(new ModuleActivity(moduleId, permission, detail, allowed, DateTime.UtcNow));

        // Trim from the front so the record stays bounded and keeps the most recent
        // activity, which is what a user investigating current behaviour needs.
        while (queue.Count > MaxActivityPerModule && queue.TryDequeue(out _))
        {
        }
    }
}

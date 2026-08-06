using System.Collections.Concurrent;
using Cayrast.Abstractions.Settings;

namespace Cayrast.Core.Settings;

/// <summary>Holds every setting descriptor, core and module alike.</summary>
public interface ISettingsRegistry
{
    /// <summary>Every registered setting, ordered by category then label.</summary>
    IReadOnlyList<SettingDescriptor> All { get; }

    /// <summary>Adds a descriptor, replacing any with the same id.</summary>
    void Register(SettingDescriptor descriptor);

    /// <summary>Removes every setting contributed by a module.</summary>
    void UnregisterModule(string moduleId);

    /// <summary>Looks a setting up by id.</summary>
    SettingDescriptor? Find(string settingId);
}

/// <summary>
/// The settings registry.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires settings to be searchable, and that is only tractable if
/// settings are data rather than hand-built interface. Every setting registers a
/// descriptor here, and one registry then drives three things at once: the settings
/// screen is generated from descriptors, settings search is a query over the same
/// descriptors, and modules get settings pages identical to the built-in ones for free.
/// </para>
/// <para>
/// The alternative — hand-writing each page and separately maintaining a search index —
/// guarantees the two drift apart. Across fourteen modules that drift is not
/// hypothetical.
/// </para>
/// </remarks>
public sealed class SettingsRegistry : ISettingsRegistry
{
    private readonly ConcurrentDictionary<string, SettingDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry, populated with Cayrast's own settings.</summary>
    public SettingsRegistry()
    {
        foreach (var descriptor in BuiltInSettings.All)
        {
            Register(descriptor);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SettingDescriptor> All =>
        [.. _descriptors.Values
            .OrderBy(descriptor => descriptor.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Label, StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc />
    public void Register(SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);

        _descriptors[descriptor.Id] = descriptor;
    }

    /// <inheritdoc />
    public void UnregisterModule(string moduleId)
    {
        foreach (var id in _descriptors
                     .Where(pair => string.Equals(pair.Value.OwnerModuleId, moduleId, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _descriptors.TryRemove(id, out _);
        }
    }

    /// <inheritdoc />
    public SettingDescriptor? Find(string settingId) =>
        _descriptors.TryGetValue(settingId, out var descriptor) ? descriptor : null;
}

/// <summary>
/// Descriptors for Cayrast's own settings.
/// </summary>
/// <remarks>
/// <para>
/// Keywords are populated generously and deliberately. Users search for the concept in
/// their head, not the label a developer happened to pick — someone looking for the
/// transparency slider is as likely to type "glass", "acrylic", or "see-through" as
/// "transparency". A sparse keyword list is the difference between settings search
/// working and merely existing.
/// </para>
/// </remarks>
public static class BuiltInSettings
{
    /// <summary>Every core setting descriptor.</summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } =
    [
        // -------------------------------------------------------- Appearance
        new()
        {
            Id = "appearance.theme",
            Category = "Appearance",
            Label = "Theme",
            Description = "Follow Windows, or force light or dark.",
            Kind = SettingKind.Choice,
            DefaultValue = nameof(ThemeMode.System),
            Choices = [("System", "Follow Windows"), ("Light", "Light"), ("Dark", "Dark")],
            Keywords = ["dark mode", "light mode", "colour scheme", "color scheme", "appearance", "night"],
        },
        new()
        {
            Id = "appearance.accentColor",
            Category = "Appearance",
            Label = "Accent colour",
            Description = "Used for highlights and matched characters in results.",
            Kind = SettingKind.Color,
            DefaultValue = AppearanceSettings.DefaultAccentColor,
            Keywords = ["accent", "colour", "color", "highlight", "brand", "tint"],
        },
        new()
        {
            Id = "appearance.transparency",
            Category = "Appearance",
            Label = "Background opacity",
            Description = "How solid the launcher background is.",
            Kind = SettingKind.Slider,
            DefaultValue = 0.85,
            Minimum = 0.2,
            Maximum = 1.0,
            Keywords = ["transparency", "opacity", "glass", "acrylic", "see through", "translucent", "blur"],
        },
        new()
        {
            Id = "appearance.borderRadius",
            Category = "Appearance",
            Label = "Corner radius",
            Kind = SettingKind.Slider,
            DefaultValue = 12,
            Minimum = 0,
            Maximum = 48,
            Keywords = ["corners", "rounded", "radius", "square", "shape"],
        },
        new()
        {
            Id = "appearance.animationSpeed",
            Category = "Appearance",
            Label = "Animation speed",
            Description = "Set to zero to switch animation off entirely.",
            Kind = SettingKind.Slider,
            DefaultValue = 1.0,
            Minimum = 0.0,
            Maximum = 3.0,
            Keywords = ["animation", "motion", "speed", "transitions", "disable animation", "reduce motion"],
        },
        new()
        {
            Id = "appearance.dockPosition",
            Category = "Appearance",
            Label = "Position",
            Kind = SettingKind.Choice,
            DefaultValue = nameof(DockPosition.Center),
            Choices =
            [
                ("Center", "Centred"), ("Top", "Top"), ("Bottom", "Bottom"),
                ("Left", "Left"), ("Right", "Right"),
            ],
            Keywords = ["position", "placement", "where", "location", "dock", "align"],
        },
        new()
        {
            Id = "appearance.panelWidth",
            Category = "Appearance",
            Label = "Panel width",
            Kind = SettingKind.Integer,
            DefaultValue = 720,
            Minimum = 320,
            Maximum = 2000,
            Keywords = ["width", "size", "wide", "narrow", "panel"],
        },
        new()
        {
            Id = "appearance.uiScale",
            Category = "Appearance",
            Label = "Interface scale",
            Description = "Applied on top of the Windows display scaling.",
            Kind = SettingKind.Slider,
            DefaultValue = 1.0,
            Minimum = 0.5,
            Maximum = 3.0,
            Keywords = ["scale", "zoom", "bigger", "smaller", "font size", "dpi", "accessibility"],
        },
        new()
        {
            Id = "appearance.respectReducedMotion",
            Category = "Appearance",
            Label = "Honour the system reduced-motion setting",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["reduced motion", "accessibility", "vestibular", "animation", "motion sickness"],
        },

        // -------------------------------------------------------- Behaviour
        new()
        {
            Id = "behavior.hotkey",
            Category = "Behaviour",
            Label = "Hotkey",
            Description = "The key combination that opens Cayrast.",
            Kind = SettingKind.Hotkey,
            DefaultValue = "Alt+Space",
            Keywords = ["hotkey", "shortcut", "keybinding", "open", "alt space", "keyboard"],
        },
        new()
        {
            Id = "behavior.launchAtStartup",
            Category = "Behaviour",
            Label = "Start with Windows",
            Kind = SettingKind.Boolean,
            DefaultValue = false,
            Keywords = ["startup", "boot", "login", "autostart", "start with windows", "run at startup"],
        },
        new()
        {
            Id = "behavior.hideOnFocusLoss",
            Category = "Behaviour",
            Label = "Hide when focus is lost",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["hide", "focus", "dismiss", "close", "click away", "auto hide"],
        },
        new()
        {
            Id = "behavior.showOnActiveMonitor",
            Category = "Behaviour",
            Label = "Open on the monitor with the cursor",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["monitor", "screen", "display", "multi monitor", "which screen"],
        },
        new()
        {
            Id = "behavior.showTrayIcon",
            Category = "Behaviour",
            Label = "Show the tray icon",
            Description = "Turning this off keeps Cayrast running, reachable only by its hotkey.",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["tray", "system tray", "notification area", "icon", "hidden", "hide icon"],
        },

        // -------------------------------------------------------- Search
        new()
        {
            Id = "search.maxResults",
            Category = "Search",
            Label = "Maximum results",
            Kind = SettingKind.Integer,
            DefaultValue = 25,
            Minimum = 1,
            Maximum = 200,
            Keywords = ["results", "how many", "limit", "count", "list length"],
        },
        new()
        {
            Id = "search.debounceMilliseconds",
            Category = "Search",
            Label = "Typing delay",
            Description = "How long to wait after a keystroke before searching.",
            Kind = SettingKind.Integer,
            DefaultValue = 40,
            Minimum = 0,
            Maximum = 250,
            Keywords = ["debounce", "delay", "lag", "responsiveness", "typing", "speed"],
        },

        // -------------------------------------------------------- Privacy
        new()
        {
            Id = "privacy.enableBrowserHistory",
            Category = "Privacy",
            Label = "Search browser history and bookmarks",
            Description = "Off by default. Browsing history is the most sensitive data Cayrast can reach.",
            Kind = SettingKind.Boolean,
            DefaultValue = false,
            Keywords = ["browser", "history", "bookmarks", "chrome", "edge", "firefox", "privacy"],
        },
        new()
        {
            Id = "privacy.enableClipboardHistory",
            Category = "Privacy",
            Label = "Keep clipboard history",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["clipboard", "history", "copy", "paste", "remember"],
        },
        new()
        {
            Id = "privacy.encryptClipboard",
            Category = "Privacy",
            Label = "Encrypt the clipboard store",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["encrypt", "clipboard", "security", "dpapi", "protect"],
        },
        new()
        {
            Id = "privacy.respectClipboardExclusions",
            Category = "Privacy",
            Label = "Skip content marked sensitive",
            Description = "Honours the clipboard flags password managers set to exclude their entries.",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["password", "sensitive", "exclude", "manager", "secret", "clipboard"],
        },

        // -------------------------------------------------------- Updates
        new()
        {
            Id = "updates.checkAutomatically",
            Category = "Updates",
            Label = "Check for updates automatically",
            Kind = SettingKind.Boolean,
            DefaultValue = true,
            Keywords = ["update", "upgrade", "version", "new release", "check"],
        },
        new()
        {
            Id = "updates.includePrerelease",
            Category = "Updates",
            Label = "Include prerelease versions",
            Kind = SettingKind.Boolean,
            DefaultValue = false,
            Keywords = ["beta", "prerelease", "preview", "alpha", "unstable", "early"],
        },
        new()
        {
            Id = "updates.automaticallyInstall",
            Category = "Updates",
            Label = "Install updates automatically",
            Description = "Off by default. Cayrast will never replace itself without asking.",
            Kind = SettingKind.Boolean,
            DefaultValue = false,
            Keywords = ["auto update", "automatic", "install", "silent", "background"],
        },
    ];
}

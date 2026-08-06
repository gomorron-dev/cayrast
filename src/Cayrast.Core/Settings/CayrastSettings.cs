using System.Text.Json.Serialization;
using Cayrast.Abstractions.Input;

namespace Cayrast.Core.Settings;

/// <summary>Which colour scheme the interface follows.</summary>
public enum ThemeMode
{
    /// <summary>Follow the Windows app theme, and change live when the user changes it.</summary>
    System = 0,

    /// <summary>Always light.</summary>
    Light = 1,

    /// <summary>Always dark.</summary>
    Dark = 2,

    /// <summary>Use an installed theme package.</summary>
    Custom = 3,
}

/// <summary>Where the launcher appears on screen.</summary>
public enum DockPosition
{
    /// <summary>Horizontally centred, slightly above centre vertically.</summary>
    /// <remarks>
    /// The default. Sitting a little above true centre matches where the eye already
    /// rests and where every comparable launcher puts itself.
    /// </remarks>
    Center = 0,

    /// <summary>Anchored near the top edge.</summary>
    Top = 1,

    /// <summary>Anchored near the bottom edge.</summary>
    Bottom = 2,

    /// <summary>Anchored to the left edge.</summary>
    Left = 3,

    /// <summary>Anchored to the right edge.</summary>
    Right = 4,

    /// <summary>A user-chosen position, remembered per monitor.</summary>
    Custom = 5,
}

/// <summary>
/// The complete user configuration, serialised to
/// <c>%APPDATA%\Cayrast\Settings\settings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Immutable. Changing a setting produces a new instance through <c>with</c>, which
/// means a settings snapshot handed to a background task cannot change underneath it
/// mid-operation.
/// </para>
/// <para>
/// Every property has a default, so a missing or truncated file degrades to sane
/// behaviour rather than failing to start.
/// </para>
/// </remarks>
public sealed record CayrastSettings
{
    /// <summary>
    /// Version of this file's shape. Bump when a migration is required.
    /// </summary>
    /// <remarks>
    /// Stored so that a settings file written by a newer Cayrast can be recognised and
    /// left alone rather than silently misread — downgrade is a real scenario when a
    /// user rolls back a bad update.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version this file was written with.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Colours, effects, and layout.</summary>
    [JsonPropertyName("appearance")]
    public AppearanceSettings Appearance { get; init; } = new();

    /// <summary>How the launcher opens, closes, and behaves.</summary>
    [JsonPropertyName("behavior")]
    public BehaviorSettings Behavior { get; init; } = new();

    /// <summary>Search categories and result limits.</summary>
    [JsonPropertyName("search")]
    public SearchSettings Search { get; init; } = new();

    /// <summary>Data collection and sensitive sources.</summary>
    [JsonPropertyName("privacy")]
    public PrivacySettings Privacy { get; init; } = new();

    /// <summary>Update checking behaviour.</summary>
    [JsonPropertyName("updates")]
    public UpdateSettings Updates { get; init; } = new();

    /// <summary>
    /// Returns a copy with every value guaranteed present and in range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always call this after deserialising. Property initialisers are not sufficient
    /// on their own for two reasons, both observed rather than theoretical:
    /// </para>
    /// <list type="bullet">
    ///   <item>An explicit <c>null</c> in the file overwrites a non-nullable property
    ///   with null, producing a <see cref="NullReferenceException"/> far from the file
    ///   that caused it.</item>
    ///   <item>Whether an absent property keeps its initialiser differs between the
    ///   reflection serialiser and the source generator, so relying on it makes
    ///   correctness depend on which one happens to be in use.</item>
    /// </list>
    /// <para>
    /// Clamping matters just as much. Settings files are meant to be hand-edited, and
    /// a transparency of <c>5.0</c> or a panel width of <c>-100</c> would otherwise
    /// produce an invisible or unusable window with no clue as to why.
    /// </para>
    /// </remarks>
    public CayrastSettings Normalized() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Appearance = (Appearance ?? new AppearanceSettings()).Normalized(),
        Behavior = (Behavior ?? new BehaviorSettings()).Normalized(),
        Search = (Search ?? new SearchSettings()).Normalized(),
        Privacy = Privacy ?? new PrivacySettings(),
        Updates = Updates ?? new UpdateSettings(),
    };
}

/// <summary>Colours, effects, and layout.</summary>
public sealed record AppearanceSettings
{
    /// <summary>The project's default accent, a warm neutral.</summary>
    public const string DefaultAccentColor = "#8d8473";

    /// <summary>Colour scheme.</summary>
    [JsonPropertyName("theme")]
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>Identifier of the installed theme used when <see cref="Theme"/> is Custom.</summary>
    [JsonPropertyName("customThemeId")]
    public string? CustomThemeId { get; init; }

    /// <summary>Accent colour as a hex string.</summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; init; } = DefaultAccentColor;

    /// <summary>Whether to take the accent from the current Windows accent colour instead.</summary>
    [JsonPropertyName("useSystemAccent")]
    public bool UseSystemAccent { get; init; }

    /// <summary>Where the launcher appears.</summary>
    [JsonPropertyName("dockPosition")]
    public DockPosition DockPosition { get; init; } = DockPosition.Center;

    /// <summary>Launcher width in logical pixels.</summary>
    [JsonPropertyName("panelWidth")]
    public int PanelWidth { get; init; } = 720;

    /// <summary>Maximum launcher height in logical pixels, before results scroll.</summary>
    [JsonPropertyName("panelMaxHeight")]
    public int PanelMaxHeight { get; init; } = 520;

    /// <summary>Corner radius in logical pixels.</summary>
    [JsonPropertyName("borderRadius")]
    public int BorderRadius { get; init; } = 12;

    /// <summary>Background opacity, 0.0 (fully transparent) to 1.0 (opaque).</summary>
    [JsonPropertyName("transparency")]
    public double Transparency { get; init; } = 0.85;

    /// <summary>Backdrop blur strength, 0.0 to 1.0.</summary>
    [JsonPropertyName("blurStrength")]
    public double BlurStrength { get; init; } = 1.0;

    /// <summary>Drop shadow intensity, 0.0 to 1.0.</summary>
    [JsonPropertyName("shadowIntensity")]
    public double ShadowIntensity { get; init; } = 0.6;

    /// <summary>
    /// Animation speed multiplier. 1.0 is the designed speed; 0 disables animation.
    /// </summary>
    [JsonPropertyName("animationSpeed")]
    public double AnimationSpeed { get; init; } = 1.0;

    /// <summary>
    /// Honour the system "reduce motion" accessibility preference.
    /// </summary>
    /// <remarks>
    /// On by default. Motion sensitivity is a genuine accessibility need, and a user
    /// who has already told Windows they want less motion should not have to tell
    /// every application separately.
    /// </remarks>
    [JsonPropertyName("respectReducedMotion")]
    public bool RespectReducedMotion { get; init; } = true;

    /// <summary>UI font family. Empty means the system default.</summary>
    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; init; } = string.Empty;

    /// <summary>Interface scale multiplier, applied on top of system DPI scaling.</summary>
    [JsonPropertyName("uiScale")]
    public double UiScale { get; init; } = 1.0;

    /// <summary>Returns a copy with missing values defaulted and numbers clamped.</summary>
    /// <remarks>
    /// The bounds are chosen so that no permitted value can produce an unusable
    /// launcher. A panel narrower than 320 px cannot show a result row legibly, and a
    /// fully transparent one would be invisible with no way to find it again.
    /// </remarks>
    public AppearanceSettings Normalized() => this with
    {
        AccentColor = string.IsNullOrWhiteSpace(AccentColor) ? DefaultAccentColor : AccentColor.Trim(),
        FontFamily = FontFamily ?? string.Empty,
        PanelWidth = Math.Clamp(PanelWidth, 320, 2000),
        PanelMaxHeight = Math.Clamp(PanelMaxHeight, 120, 2000),
        BorderRadius = Math.Clamp(BorderRadius, 0, 48),

        // Never fully transparent: an invisible launcher cannot be dismissed or found.
        Transparency = Math.Clamp(Transparency, 0.2, 1.0),
        BlurStrength = Math.Clamp(BlurStrength, 0.0, 1.0),
        ShadowIntensity = Math.Clamp(ShadowIntensity, 0.0, 1.0),

        // Zero is meaningful — it disables animation — so the lower bound is 0.
        AnimationSpeed = Math.Clamp(AnimationSpeed, 0.0, 3.0),
        UiScale = Math.Clamp(UiScale, 0.5, 3.0),
    };
}

/// <summary>How the launcher opens, closes, and behaves.</summary>
public sealed record BehaviorSettings
{
    /// <summary>Global hotkey, stored in readable form such as <c>Alt+Space</c>.</summary>
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; init; } = HotkeyBinding.Default.ToString();

    /// <summary>Start Cayrast when the user signs in.</summary>
    /// <remarks>
    /// Off by default. A launcher that adds itself to startup uninvited is exactly the
    /// behaviour that makes people distrust utilities; the first-run wizard asks instead.
    /// </remarks>
    [JsonPropertyName("launchAtStartup")]
    public bool LaunchAtStartup { get; init; }

    /// <summary>Hide the launcher when it loses focus.</summary>
    [JsonPropertyName("hideOnFocusLoss")]
    public bool HideOnFocusLoss { get; init; } = true;

    /// <summary>Clear the query when hiding, so the next open starts fresh.</summary>
    /// <remarks>
    /// On by default. Reopening to a query typed twenty minutes ago is confusing far
    /// more often than it is useful.
    /// </remarks>
    [JsonPropertyName("clearQueryOnHide")]
    public bool ClearQueryOnHide { get; init; } = true;

    /// <summary>Show on the monitor containing the cursor rather than the primary one.</summary>
    [JsonPropertyName("showOnActiveMonitor")]
    public bool ShowOnActiveMonitor { get; init; } = true;

    /// <summary>Show the tray icon.</summary>
    /// <remarks>Turning this off is "hidden mode" — the launcher keeps working via its hotkey.</remarks>
    [JsonPropertyName("showTrayIcon")]
    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>Returns a copy with a usable hotkey string.</summary>
    /// <remarks>
    /// Only presence is checked here; whether the combination parses is decided when
    /// it is registered, which is also where the user can be told it is unavailable.
    /// </remarks>
    public BehaviorSettings Normalized() => this with
    {
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? HotkeyBinding.Default.ToString() : Hotkey.Trim(),
    };
}

/// <summary>Search categories and result limits.</summary>
public sealed record SearchSettings
{
    /// <summary>Category ids the user has enabled.</summary>
    [JsonPropertyName("enabledCategories")]
    public IReadOnlyList<string> EnabledCategories { get; init; } =
        ["applications", "commands", "files", "clipboard", "settings", "tools", "system"];

    /// <summary>Maximum results shown across all providers.</summary>
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; init; } = 25;

    /// <summary>
    /// Milliseconds to wait after a keystroke before dispatching a query.
    /// </summary>
    /// <remarks>
    /// Zero would re-query on every character, wasting work the user has already
    /// superseded; too high and typing feels laggy. Around 40 ms is below the
    /// threshold where a delay becomes perceptible while still collapsing the bursts
    /// that happen mid-word.
    /// </remarks>
    [JsonPropertyName("debounceMilliseconds")]
    public int DebounceMilliseconds { get; init; } = 40;

    /// <summary>Directories included in file search.</summary>
    /// <remarks>Empty means the user profile, resolved at runtime rather than stored.</remarks>
    [JsonPropertyName("indexedFolders")]
    public IReadOnlyList<string> IndexedFolders { get; init; } = [];

    /// <summary>Returns a copy with non-null collections and clamped limits.</summary>
    public SearchSettings Normalized() => this with
    {
        // A null list here would fault on the very first keystroke.
        EnabledCategories = EnabledCategories ?? [],
        IndexedFolders = IndexedFolders ?? [],

        // An unbounded result count would let one query build a list long enough to
        // stall rendering; one below 1 would show nothing and look broken.
        MaxResults = Math.Clamp(MaxResults, 1, 200),

        // Above roughly a quarter second the launcher stops feeling responsive.
        DebounceMilliseconds = Math.Clamp(DebounceMilliseconds, 0, 250),
    };
}

/// <summary>Data collection and access to sensitive sources.</summary>
/// <remarks>
/// Every field here defaults to the private choice. A user who never opens settings
/// gets the most private configuration, not the most featureful one.
/// </remarks>
public sealed record PrivacySettings
{
    /// <summary>
    /// Allow searching browser history and bookmarks.
    /// </summary>
    /// <remarks>
    /// Off by default and deliberately opt-in. Browsing history is the most sensitive
    /// data Cayrast could touch, and surfacing it in a launcher that opens over
    /// whatever the user is sharing on a call is a real hazard.
    /// </remarks>
    [JsonPropertyName("enableBrowserHistory")]
    public bool EnableBrowserHistory { get; init; }

    /// <summary>Keep clipboard history.</summary>
    [JsonPropertyName("enableClipboardHistory")]
    public bool EnableClipboardHistory { get; init; } = true;

    /// <summary>Encrypt the clipboard store with DPAPI.</summary>
    [JsonPropertyName("encryptClipboard")]
    public bool EncryptClipboard { get; init; } = true;

    /// <summary>Exclude clipboard entries copied from password managers.</summary>
    /// <remarks>
    /// Honours the standard <c>ExcludeClipboardContentFromMonitorProcessing</c> and
    /// <c>CanIncludeInClipboardHistory</c> clipboard formats that password managers
    /// set. Ignoring these would turn clipboard history into a plaintext password log.
    /// </remarks>
    [JsonPropertyName("respectClipboardExclusions")]
    public bool RespectClipboardExclusions { get; init; } = true;
}

/// <summary>Update checking behaviour.</summary>
public sealed record UpdateSettings
{
    /// <summary>Check GitHub Releases for a newer version.</summary>
    [JsonPropertyName("checkAutomatically")]
    public bool CheckAutomatically { get; init; } = true;

    /// <summary>Include prerelease versions when checking.</summary>
    [JsonPropertyName("includePrerelease")]
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Download and install updates without asking.
    /// </summary>
    /// <remarks>
    /// Off by default, and it stays a choice. Silently replacing software someone
    /// depends on is not acceptable behaviour for a tool that sits in the way of
    /// everything they do.
    /// </remarks>
    [JsonPropertyName("automaticallyInstall")]
    public bool AutomaticallyInstall { get; init; }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cayrast.Core.Settings;

/// <summary>
/// Source-generated JSON metadata for the settings tree.
/// </summary>
/// <remarks>
/// Using a generated context rather than reflection keeps startup fast (no reflection
/// warm-up on the path that delays sign-in) and keeps the app trim- and AOT-compatible,
/// which matters for keeping the installed size down later.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    // Settings files are hand-editable by design; a trailing comma or a comment left
    // by a user tweaking the file should not brick their configuration.
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CayrastSettings))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

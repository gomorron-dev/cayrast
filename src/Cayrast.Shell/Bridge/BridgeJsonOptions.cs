using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cayrast.Shell.Bridge;

/// <summary>Serialisation settings shared by everything crossing the bridge.</summary>
/// <remarks>
/// Centralised because both sides must agree exactly. The frontend expects camelCase
/// property names and string enum values; changing either here without changing the
/// TypeScript definitions produces silent nulls rather than an error, which is a
/// tedious class of bug to chase.
/// </remarks>
internal static class BridgeJsonOptions
{
    /// <summary>The shared options instance.</summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

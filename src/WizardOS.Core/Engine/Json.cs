using System.Text.Json;
using System.Text.Json.Serialization;

namespace WizardOS.Core.Engine;

internal static class EngineJson
{
    public static readonly JsonSerializerOptions Options = Create(false);
    public static readonly JsonSerializerOptions Compact = Create(false);
    public static readonly JsonSerializerOptions Indented = Create(true);

    private static JsonSerializerOptions Create(bool indent)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = indent,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}

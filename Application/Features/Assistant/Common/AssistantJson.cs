using System.Text.Json;
using System.Text.Json.Serialization;

namespace APCS.Application.Features.Assistant.Common;

public static class AssistantJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static JsonElement SerializeToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string Truncate(string json, int maxChars)
    {
        if (json.Length <= maxChars)
            return json;
        var preview = json[..Math.Max(0, maxChars - 64)];
        return Serialize(new { truncated = true, preview });
    }
}

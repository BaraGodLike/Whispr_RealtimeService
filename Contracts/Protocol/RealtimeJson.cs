using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts.Protocol;

public static class RealtimeJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(ServerEnvelope<T> envelope)
    {
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    public static ClientEnvelope? DeserializeClientEnvelope(string json)
    {
        return JsonSerializer.Deserialize<ClientEnvelope>(json, SerializerOptions);
    }

    public static T? DeserializeData<T>(JsonElement element)
    {
        return element.Deserialize<T>(SerializerOptions);
    }
}

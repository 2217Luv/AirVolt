using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirVolt.NativeHelper.Protocol;

public class JsonRpcRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class JsonRpcEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}

public static class JsonRpcSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static JsonRpcRequest? DeserializeRequest(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonRpcRequest>(line, Options);
        }
        catch
        {
            return null;
        }
    }

    public static string SerializeResponse(JsonRpcResponse response)
    {
        return JsonSerializer.Serialize(response, Options);
    }

    public static string SerializeEvent(JsonRpcEvent evt)
    {
        return JsonSerializer.Serialize(evt, Options);
    }
}

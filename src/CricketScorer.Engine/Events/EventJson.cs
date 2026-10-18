using System.Text.Json;
using System.Text.Json.Serialization;

namespace CricketScorer.Engine;

public static class EventJson
{
    public static readonly JsonSerializerOptions Options = Create();

    // Applies the shared event JSON rules: string enums and a "type" property allowed anywhere.
    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.AllowOutOfOrderMetadataProperties = true;
    }

    // Writes an event as JSON in the shared format.
    public static string Serialize(MatchEvent e) => JsonSerializer.Serialize(e, Options);

    // Reads an event from JSON in the shared format.
    public static MatchEvent Deserialize(string json) =>
        JsonSerializer.Deserialize<MatchEvent>(json, Options) ?? throw new JsonException("Empty event.");

    // Builds the options: web defaults (camelCase, case-insensitive) plus the shared rules.
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}

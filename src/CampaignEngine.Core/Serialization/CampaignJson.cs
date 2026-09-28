using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CampaignEngine.Core.Campaigns;

namespace CampaignEngine.Core.Serialization;

/// <summary>
/// The JSON contract shared by the HTTP API, the database and local (snapshot) evaluation.
/// </summary>
/// <remarks>
/// Tuned for integration with hand-written JSON from any language:
/// camelCase names, enums as camelCase strings, the <c>"type"</c> discriminator may appear anywhere in
/// the object, numbers may be sent as strings (<c>"19.90"</c>), unknown properties are ignored,
/// and times of day accept both <c>"10:00"</c> and <c>"10:00:00"</c>.
/// </remarks>
public static class CampaignJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applies the contract settings to existing options (e.g. ASP.NET Core's).</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.AllowOutOfOrderMetadataProperties = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new TimeOnlyConverter());
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("JSON value is null.");

    public static Campaign Clone(Campaign campaign) => Deserialize<Campaign>(Serialize(campaign));

    private sealed class TimeOnlyConverter : JsonConverter<TimeOnly>
    {
        private static readonly string[] Formats = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"];

        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            return TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time
                : throw new JsonException($"'{text}' is not a valid time of day (expected HH:mm).");
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(value.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using StormBench.Domain.Control;
using StormBench.Domain.Network;
using StormBench.Domain.Runs;
using StormBench.Domain.Scenarios;

namespace StormBench.Scada.Endpoints;

/// <summary>
/// The domain's value types on the wire of the HTTP API: ids as their text, volumes as plain
/// cubic metres, scenario and run states and the control center as lower-case names.
/// </summary>
public static class DomainJson
{
    extension(JsonSerializerOptions options)
    {
        public JsonSerializerOptions AddDomainConverters()
        {
            options.Converters.Add(new IdentifierConverter<TankId>());
            options.Converters.Add(new IdentifierConverter<GateId>());
            options.Converters.Add(new IdentifierConverter<OverflowPointId>());
            options.Converters.Add(new IdentifierConverter<StormId>());
            options.Converters.Add(new IdentifierConverter<AlgorithmId>());
            options.Converters.Add(new IdentifierConverter<RunId>());
            options.Converters.Add(new VolumeConverter());
            options.Converters.Add(new JsonStringEnumConverter<ScenarioState>(JsonNamingPolicy.CamelCase));
            options.Converters.Add(new JsonStringEnumConverter<RunState>(JsonNamingPolicy.CamelCase));
            options.Converters.Add(new JsonStringEnumConverter<ControlCenter>(JsonNamingPolicy.CamelCase));
            return options;
        }
    }
}

/// <summary>An id as its text.</summary>
public sealed class IdentifierConverter<T> : JsonConverter<T>
    where T : struct, IParsable<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        T.Parse(reader.GetString() ?? throw new JsonException($"{typeof(T).Name} must be a string"), null);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>A volume as its number of cubic metres.</summary>
public sealed class VolumeConverter : JsonConverter<Volume>
{
    public override Volume Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetDouble());

    public override void Write(Utf8JsonWriter writer, Volume value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value.CubicMeters);
    }
}

using AAEmu.Game.Models.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Utils.Converters;

//Convert an object to its minimalistic json representation
public class JsonDoodadSpawnsConverter : BaseJsonConverter<JsonDoodadSpawns>
{
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var json = JObject.Load(reader);
        return new JsonDoodadSpawns
        {
            Id = json[nameof(JsonDoodadSpawns.Id)]?.Value<uint>() ?? 0,
            UnitId = json[nameof(JsonDoodadSpawns.UnitId)]?.Value<uint>() ?? 0,
            Title = json[nameof(JsonDoodadSpawns.Title)]?.Value<string>() ?? string.Empty,
            RelatedIds = json[nameof(JsonDoodadSpawns.RelatedIds)]?.ToObject<List<uint>>(serializer) ?? [],
            Position = json[nameof(JsonDoodadSpawns.Position)]?.ToObject<JsonPosition>(serializer) ?? new JsonPosition(),
            FuncGroupId = json[nameof(JsonDoodadSpawns.FuncGroupId)]?.Value<uint>() ?? 0,
            Scale = json[nameof(JsonDoodadSpawns.Scale)]?.Value<float>() ?? 0f
        };
    }

    public override void WriteJson(JsonWriter writer, JsonDoodadSpawns value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(nameof(value.Id));
        writer.WriteValue(value.Id);
        writer.WritePropertyName(nameof(value.UnitId));
        writer.WriteValue(value.UnitId);
        writer.WritePropertyName(nameof(value.Title));
        writer.WriteValue(value.Title);
        if (value.RelatedIds is { Count: > 0 })
        {
            writer.WritePropertyName(nameof(value.RelatedIds));
            serializer.Serialize(writer, value.RelatedIds);
        }
        writer.WritePropertyName(nameof(value.Position));
        serializer.Serialize(writer, value.Position);
        writer.WritePropertyName(nameof(value.FuncGroupId));
        writer.WriteValue(value.FuncGroupId);
        writer.WritePropertyName(nameof(value.Scale));
        writer.WriteValue(value.Scale);
        writer.WriteEndObject();
    }
}

using AAEmu.Game.Models.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Utils.Converters;

//Convert an object to its minimalistic json representation
public class JsonQuestSphereConverter : BaseJsonConverter<JsonQuestSphere>
{
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var json = JObject.Load(reader);
        return new JsonQuestSphere
        {
            Id = json[nameof(JsonQuestSphere.Id)]?.Value<uint>() ?? 0,
            QuestId = json[nameof(JsonQuestSphere.QuestId)]?.Value<uint>() ?? 0,
            SphereId = json[nameof(JsonQuestSphere.SphereId)]?.Value<uint>() ?? 0,
            Radius = json[nameof(JsonQuestSphere.Radius)]?.Value<float>() ?? 0f,
            Position = json[nameof(JsonQuestSphere.Position)]?.ToObject<JsonPosition>(serializer) ?? new JsonPosition()
        };
    }

    public override void WriteJson(JsonWriter writer, JsonQuestSphere value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(nameof(value.Id));
        writer.WriteValue(value.Id);
        writer.WritePropertyName(nameof(value.QuestId));
        writer.WriteValue(value.QuestId);
        writer.WritePropertyName(nameof(value.SphereId));
        writer.WriteValue(value.SphereId);
        writer.WritePropertyName(nameof(value.Radius));
        writer.WriteValue(value.Radius);
        writer.WritePropertyName(nameof(value.Position));
        serializer.Serialize(writer, value.Position);
        writer.WriteEndObject();
    }
}

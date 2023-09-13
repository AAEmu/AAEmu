using System;
using AAEmu.Game.Models.Json;
using Newtonsoft.Json;

namespace AAEmu.Game.Utils.Converters
{
    //Convert an object to its minimalistic json representation
    public class JsonQuestSphereConverter : BaseJsonConverter<JsonQuestSphere>
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
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
}

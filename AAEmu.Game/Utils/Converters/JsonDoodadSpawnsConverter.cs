using System;
using AAEmu.Game.Models.Json;
using Newtonsoft.Json;

namespace AAEmu.Game.Utils.Converters
{
    //Convert an object to its minimalistic json representation
    public class JsonDoodadSpawnsConverter : BaseJsonConverter<JsonDoodadSpawns>
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, JsonDoodadSpawns value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Id));
            writer.WriteValue(value.Id);
            writer.WritePropertyName(nameof(value.UnitId));
            writer.WriteValue(value.UnitId);
            writer.WritePropertyName(nameof(value.Position));
            serializer.Serialize(writer, value.Position);
            writer.WriteEndObject();
        }
    }
}

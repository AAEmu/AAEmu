using System;
using AAEmu.Game.Models.Json;
using Newtonsoft.Json;

namespace AAEmu.Game.Utils.Converters
{
    //Convert an object to its minimalistic json representation
    public class JsonPositionConverter : BaseJsonConverter<JsonPosition>
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, JsonPosition value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.X));
            writer.WriteValue(value.X);
            writer.WritePropertyName(nameof(value.Y));
            writer.WriteValue(value.Y);
            writer.WritePropertyName(nameof(value.Z));
            writer.WriteValue(value.Z);

            //Yaw Pitch and Roll properties don't need to be extracted to json if they have 0 default value
            if (value.Roll != default)
            {
                writer.WritePropertyName(nameof(value.Roll));
                writer.WriteRawValue(value.Roll.ToString("0.####"));
            }
            if (value.Yaw != default)
            {
                writer.WritePropertyName(nameof(value.Yaw));
                writer.WriteRawValue(value.Yaw.ToString("0.####"));
            }
            if (value.Pitch != default)
            {
                writer.WritePropertyName(nameof(value.Pitch));
                writer.WriteRawValue(value.Pitch.ToString("0.####"));
            }
            writer.WriteEndObject();
        }
    }
}

using AAEmu.Game.Models.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Utils.Converters;

//Convert an object to its minimalistic json representation
public class JsonPositionConverter : BaseJsonConverter<JsonPosition>
{
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        var pos = new JsonPosition
        {
            X = jo["X"]?.Value<float>() ?? default,
            Y = jo["Y"]?.Value<float>() ?? default,
            Z = jo["Z"]?.Value<float>() ?? default,
            Roll = jo["Roll"]?.Value<float>() ?? default,
            Yaw = jo["Yaw"]?.Value<float>() ?? default,
            Pitch = jo["Pitch"]?.Value<float>() ?? default
        };

        return pos;
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

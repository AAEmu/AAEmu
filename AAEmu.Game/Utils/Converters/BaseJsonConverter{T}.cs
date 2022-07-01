using System;
using Newtonsoft.Json;

namespace AAEmu.Game.Utils.Converters
{
    public abstract class BaseJsonConverter<T> : JsonConverter where T : class
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(T);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            WriteJson(writer, (T)value, serializer);
        }

        public abstract void WriteJson(JsonWriter writer, T value, JsonSerializer serializer);
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AAEmu.Game.Utils.Converters
{
    public class ModelJsonConverter : JsonConverter
    {
        private Dictionary<Type, JsonConverter> _converters = new Dictionary<Type, JsonConverter>
        {
            {
                typeof(JsonDoodadPosConverter), new JsonDoodadPosConverter()
            }
        };
        public ModelJsonConverter()
        {
            AddConverter<JsonDoodadPosConverter>();
        }
        public void AddConverter<T>() where T : JsonConverter 
        {
            if (!_converters.ContainsKey(typeof(T)))
            {
                _converters.Add(typeof(T), (T)Activator.CreateInstance(typeof(T)));
            }
        }

        public override bool CanConvert(Type objectType)
        {
            return _converters.ContainsKey(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return _converters[objectType].ReadJson(reader, objectType, existingValue, serializer);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            _converters[value.GetType()].WriteJson(writer, value, serializer);
        }
    }
}

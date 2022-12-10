using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Json;
using Newtonsoft.Json;

namespace AAEmu.Game.Utils.Converters
{
    public class JsonModelsConverter : JsonConverter
    {
        private Dictionary<Type, JsonConverter> _converters = new Dictionary<Type, JsonConverter>();

        public JsonModelsConverter()
        {
            AddConverter<JsonPositionConverter, JsonPosition>();
            AddConverter<JsonQuestSphereConverter, JsonQuestSphere>();
            AddConverter<JsonDoodadSpawnsConverter, JsonDoodadSpawns>();
            AddConverter<JsonNpcSpawnsConverter, JsonNpcSpawns>();
        }
        public void AddConverter<T, Y>() where T : BaseJsonConverter<Y> where Y : class
        {
            if (!_converters.ContainsKey(typeof(T)))
            {
                _converters.Add(typeof(Y), (T)Activator.CreateInstance(typeof(T)));
            }
        }

        public override bool CanConvert(Type objectType)
        {
            var canConvert = _converters.ContainsKey(objectType);
            //var canConvert = _converters.ContainsKey(objectType.IsArray ? objectType.GetElementType() : objectType);
            return canConvert;
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

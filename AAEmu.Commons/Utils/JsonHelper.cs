using System;
using Newtonsoft.Json;

namespace AAEmu.Commons.Utils
{
    public static class JsonHelper
    {
        public static T DeserializeObject<T>(string json, params JsonConverter[] converters) => JsonConvert.DeserializeObject<T>(json, converters);

        public static bool TryDeserializeObject<T>(string json, out T result, out Exception error)
        {
            result = default(T);

            if (string.IsNullOrWhiteSpace(json))
            {
                error = new ArgumentException("NullOrWhiteSpace", "json");
                return false;
            }

            try
            {
                result = JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                result = default(T);
                error = e;
                return false;
            }

            error = null;
            return result != null;
        }
    }
}

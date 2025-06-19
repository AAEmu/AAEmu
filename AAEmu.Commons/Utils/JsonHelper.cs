using System;
using Newtonsoft.Json;

namespace AAEmu.Commons.Utils;

/// <summary>
/// 提供 JSON 序列化和反序列化的辅助方法，主要使用 Newtonsoft.Json 库。
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// 将 JSON 字符串反序列化为指定类型的对象。
    /// </summary>
    /// <typeparam name="T">要反序列化到的对象的类型。</typeparam>
    /// <param name="json">要反序列化的 JSON 字符串。</param>
    /// <param name="converters">在反序列化过程中要使用的 <see cref="JsonConverter"/> 对象数组。</param>
    /// <returns>从 JSON 字符串反序列化得到的 <typeparamref name="T"/>类型的对象。</returns>
    public static T DeserializeObject<T>(string json, params JsonConverter[] converters) => JsonConvert.DeserializeObject<T>(json, converters);

    /// <summary>
    /// 尝试将 JSON 字符串反序列化为指定类型的对象，并返回一个指示操作是否成功的值。
    /// </summary>
    /// <typeparam name="T">要反序列化到的对象的类型。</typeparam>
    /// <param name="json">要反序列化的 JSON 字符串。</param>
    /// <param name="result">当此方法返回时，如果反序列化成功，则包含反序列化得到的对象；否则为 <typeparamref name="T"/>类型的默认值。此参数未经初始化即被传递。</param>
    /// <param name="error">当此方法返回时，如果反序列化过程中发生错误，则包含发生的异常；否则为 null。此参数未经初始化即被传递。</param>
    /// <returns>如果 <paramref name="json"/> 成功反序列化，则为 true；否则为 false。</returns>
    public static bool TryDeserializeObject<T>(string json, out T result, out Exception error)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = new ArgumentException("NullOrWhiteSpace", nameof(json));
            return false;
        }

        try
        {
            result = JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception e)
        {
            result = default;
            error = e;
            return false;
        }

        error = null;
        return result != null;
    }
}

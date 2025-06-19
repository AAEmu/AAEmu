using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Xml;

namespace AAEmu.Commons.Utils.XML;

/// <summary>
/// 提供用于处理 XML 数据转换和读取的辅助方法。
/// </summary>
public static class XmlHelper
{
    /// <summary>
    /// 将逗号分隔的字符串（例如 "1.0,2.5,3.7"）转换为 <see cref="Vector3"/> 对象。
    /// </summary>
    /// <param name="positionString">包含逗号分隔的 x, y, z 坐标的字符串。</param>
    /// <returns>转换后的 <see cref="Vector3"/> 对象；如果字符串格式无效或无法解析，则返回 <see cref="Vector3.Zero"/>。</returns>
    public static Vector3 StringToVector3(string positionString)
    {
        var xyz = positionString.Split(',');
        if (xyz.Length == 3)
        {
            // 使用 InvariantCulture 以确保小数点解析的一致性。
            if ((float.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) &&
                (float.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) &&
                (float.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)))
                return new Vector3(x, y, z);
        }
        return Vector3.Zero;
    }

    /// <summary>
    /// 读取给定 <see cref="XmlNode"/> 的所有属性，并将其作为名称/值对存储在字典中。
    /// </summary>
    /// <param name="node">要从中读取属性的 <see cref="XmlNode"/>。</param>
    /// <returns>一个包含节点属性的 <see cref="Dictionary{String, String}"/>；如果节点没有属性，则为空字典。</returns>
    public static Dictionary<string, string> ReadNodeAttributes(XmlNode node)
    {
        var res = new Dictionary<string, string>();
        if (node.Attributes != null)
        {
            for (var i = 0; i < node.Attributes.Count; i++)
                res.Add(node.Attributes.Item(i).Name, node.Attributes.Item(i).Value);
        }
        return res;
    }

    /// <summary>
    /// 从属性字典中读取指定字段的值，并尝试将其转换为指定的类型 <typeparamref name="T"/>。
    /// </summary>
    /// <typeparam name="T">期望转换的目标类型。</typeparam>
    /// <param name="attribs">包含属性名称和值的字典。</param>
    /// <param name="field">要读取的属性的名称（字段名）。</param>
    /// <param name="defaultValue">如果属性不存在或无法转换，则返回的默认值。</param>
    /// <returns>转换后的属性值；如果属性不存在或转换失败，则为 <paramref name="defaultValue"/>。</returns>
    public static T ReadAttribute<T>(Dictionary<string, string> attribs, string field, T defaultValue)
    {
        if (!attribs.TryGetValue(field, out var val))
            return defaultValue;

        try
        {
            // 尝试将字符串值转换为目标类型 T。
            return (T)Convert.ChangeType(val, typeof(T));
        }
        catch // 如果转换失败（例如，格式不正确），则返回默认值。
        {
            return defaultValue;
        }
    }
}

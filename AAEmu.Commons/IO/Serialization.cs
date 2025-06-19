using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;


namespace AAEmu.Commons.IO;

/// <summary>
/// 帮助将对象序列化为 XML 以及从 XML 反序列化回对象。
/// </summary>
public static class Serialization
{
    /// <summary>
    /// 将对象转换为 XML
    /// </summary>
    /// <param name="temp">要转换的对象</param>
    /// <param name="rootName">XML 的根元素名称。</param>
    /// <param name="fileName">要保存 XML 到的文件</param>
    /// <returns>XML 格式的对象的字符串表示形式</returns>
    public static string ObjectToXML(object temp, string rootName, string fileName)
    {
        var xml = ObjectToXML(temp, rootName);
        File.AppendAllText(fileName, xml);
        return xml;
    }

    /// <summary>
    /// 将对象转换为 XML
    /// </summary>
    /// <param name="temp">要转换的对象</param>
    /// <param name="rootName">XML 的根元素名称。</param>
    /// <returns>XML 格式的对象的字符串表示形式</returns>
    public static string ObjectToXML(object temp, string rootName)
    {
        if (temp == null)
            throw new ArgumentException("Object can not be null");
        using (var stream = new MemoryStream())
        {
            var serializer = new XmlSerializer(temp.GetType(), new XmlRootAttribute(rootName));
            serializer.Serialize(stream, temp);
            stream.Flush();
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
        }
    }

    /// <summary>
    /// 读取 XML 文件并导出其中包含的对象
    /// </summary>
    /// <param name="fileName">要使用的文件名</param>
    /// <param name="result">要导出到的对象</param>
    /// <param name="rootName">期望的 XML 根元素名称。</param>
    public static void XMLToObject<T>(string fileName, out T result, string rootName)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("File name can not be null/empty");
        if (!File.Exists(fileName))
            throw new ArgumentException("File does not exist");
        var content = FileManager.GetFileContents(fileName);
        result = XMLToObject<T>(content, rootName);
    }

    /// <summary>
    /// 将 XML 字符串转换为对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="xml">XML 字符串</param>
    /// <param name="rootName">期望的 XML 根元素名称。</param>
    /// <returns>指定类型的对象</returns>
    public static T XMLToObject<T>(string xml, string rootName)
    {
        if (string.IsNullOrEmpty(xml))
            throw new ArgumentException("XML can not be null/empty");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
        {
            var serializer = new XmlSerializer(typeof(T), new XmlRootAttribute(rootName));
            return (T)serializer.Deserialize(stream);
        }
    }

    /// <summary>
    /// 读取 XML 文件并导出其中包含的对象
    /// </summary>
    /// <param name="fileName">要使用的文件名</param>
    /// <param name="result">要导出到的对象</param>
    /// <param name="type">要导出的对象类型</param>
    /// <param name="rootName">期望的 XML 根元素名称。</param>
    public static void XMLToObject(string fileName, out object result, Type type, string rootName)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("File name can not be null/empty");
        if (!File.Exists(fileName))
            throw new ArgumentException("File does not exist");
        var content = FileManager.GetFileContents(fileName);
        result = XMLToObject(content, type, rootName);
    }

    /// <summary>
    /// 将 XML 字符串转换为对象
    /// </summary>
    /// <param name="xml">XML 字符串</param>
    /// <param name="type">要导出的对象类型</param>
    /// <param name="rootName">期望的 XML 根元素名称。</param>
    /// <returns>指定类型的对象</returns>
    public static object XMLToObject(string xml, Type type, string rootName)
    {
        if (string.IsNullOrEmpty(xml))
            throw new ArgumentException("XML can not be null/empty");
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
        {
            var serializer = new XmlSerializer(type, new XmlRootAttribute(rootName));
            return serializer.Deserialize(stream);
        }
    }
}

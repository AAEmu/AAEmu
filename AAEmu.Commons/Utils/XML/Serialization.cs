using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using AAEmu.Commons.IO;

namespace Process_Commons
{
	/// <summary>
	/// Helps with serializing an object to XML and back again.
	/// </summary>
	public static class Serialization
	{
		/// <summary>
		/// Converts an object to XML
		/// </summary>
		/// <param name="temp">Object to convert</param>
		/// <param name="fileName">File to save the XML to</param>
		/// <returns>string representation of the object in XML format</returns>
		public static string ObjectToXml(object temp, string rootName, string fileName)
		{
			var xml = ObjectToXml(temp, rootName);
			File.AppendAllText(fileName, xml);
			return xml;
		}

		/// <summary>
		/// Converts an object to XML
		/// </summary>
		/// <param name="temp">Object to convert</param>
		/// <returns>string representation of the object in XML format</returns>
		public static string ObjectToXml(object temp, string rootName)
		{
			if (temp == null)
			{
				throw new ArgumentException("Object can not be null");
			}

			using (var stream = new MemoryStream())
			{
				var serializer = new XmlSerializer(temp.GetType(), new XmlRootAttribute(rootName));
				serializer.Serialize(stream, temp);
				stream.Flush();
				return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
			}
		}

		/// <summary>
		/// Takes an XML file and exports the Object it holds
		/// </summary>
		/// <param name="fileName">File name to use</param>
		/// <param name="result">Object to export to</param>
		public static void XmlToObject<T>(string fileName, out T result, string rootName)
		{
			if (string.IsNullOrEmpty(fileName))
			{
				throw new ArgumentException("File name can not be null/empty");
			}

			if (!FileManager.FileExists(fileName))
			{
				throw new ArgumentException("File does not exist");
			}

			var content = FileManager.GetFileContents(fileName);
			result = XmlToObject<T>(content, rootName);
		}

		/// <summary>
		/// Converts an XML string to an object
		/// </summary>
		/// <typeparam name="T">Object type</typeparam>
		/// <param name="xml">XML string</param>
		/// <returns>The object of the specified type</returns>
		public static T XmlToObject<T>(string xml, string rootName)
		{
			if (string.IsNullOrEmpty(xml))
			{
				throw new ArgumentException("XML can not be null/empty");
			}

			using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
			{
				var serializer = new XmlSerializer(typeof(T), new XmlRootAttribute(rootName));
				return (T)serializer.Deserialize(stream);
			}
		}

		/// <summary>
		/// Takes an XML file and exports the Object it holds
		/// </summary>
		/// <param name="fileName">File name to use</param>
		/// <param name="result">Object to export to</param>
		/// <param name="type">Object type to export</param>
		public static void XmlToObject(string fileName, out object result, Type type, string rootName)
		{
			if (string.IsNullOrEmpty(fileName))
			{
				throw new ArgumentException("File name can not be null/empty");
			}

			if (!FileManager.FileExists(fileName))
			{
				throw new ArgumentException("File does not exist");
			}

			var content = FileManager.GetFileContents(fileName);
			result = XmlToObject(content, type, rootName);
		}

		/// <summary>
		/// Converts an XML string to an object
		/// </summary>
		/// <param name="xml">XML string</param>
		/// <param name="type">Object type to export</param>
		/// <returns>The object of the specified type</returns>
		public static object XmlToObject(string xml, Type type, string rootName)
		{
			if (string.IsNullOrEmpty(xml))
			{
				throw new ArgumentException("XML can not be null/empty");
			}

			using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
			{
				var serializer = new XmlSerializer(type, new XmlRootAttribute(rootName));
				return serializer.Deserialize(stream);
			}
		}
	}
}

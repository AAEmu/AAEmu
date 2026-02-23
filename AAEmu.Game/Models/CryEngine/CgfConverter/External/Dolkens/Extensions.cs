using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using CgfConverter;
using DDRIT = Dolkens.Framework.Extensions.ExtensionMethods;

namespace Dolkens.Framework.Extensions
{
    /// <summary>
    /// The MIT License (MIT)
    /// 
    /// Copyright (c) 2008 Peter Dolkens
    /// 
    /// Permission is hereby granted, free of charge, to any person obtaining a copy
    /// of this software and associated documentation files (the "Software"), to deal
    /// in the Software without restriction, including without limitation the rights
    /// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    /// copies of the Software, and to permit persons to whom the Software is
    /// furnished to do so, subject to the following conditions:
    /// 
    /// The above copyright notice and this permission notice shall be included in
    /// all copies or substantial portions of the Software.
    /// 
    /// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    /// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    /// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    /// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    /// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    /// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    /// THE SOFTWARE.
    /// </summary>
    public static class ExtensionMethods
    {
        #region Private Members

        private static readonly Dictionary<Type, MethodInfo> _parserMap = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> _byteArrayMap = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, ConstructorInfo> _byteConstructorMap = new Dictionary<Type, ConstructorInfo>();

        #endregion

        #region Serialization

        #region XML

        /// <summary>
        /// Attempt to serialize any object into a XML string.
        /// </summary>
        /// <param name="input">The object to serialize.</param>
        /// <returns>A string containing the serialized object.</returns>
        public static String ToXML(this Object input)
        {
            XmlSerializer serializer = new XmlSerializer(input.GetType());

            using (MemoryStream ms = new MemoryStream())
            {
                XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                ns.Add("", "");

                using (var xmlTextWriter = new XmlTextWriter(ms, Encoding.UTF8))
                {
                    serializer.Serialize(xmlTextWriter, input, ns);
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <param name="type">The type to convert to.</param>
        /// <returns>An Object deserialized from the string.</returns>
        public static Object FromXML(this String input, Type type)
        {
            XmlSerializer serializer = new XmlSerializer(type);

            byte[] bytes = Encoding.UTF8.GetBytes(input);

            using (var stream = new MemoryStream(bytes))
            {
                return serializer.Deserialize(stream);
            }

        }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <typeparam name="TResult">The type to convert to.</typeparam>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <returns>An Object of type T deserialized from the string.</returns>
        public static TResult FromXML<TResult>(this String input)
        {
            Object buffer = DDRIT.FromXML(input, typeof(TResult));

            return (buffer == null) ? default(TResult) : (TResult)buffer;
        }

        #endregion

        #endregion

        #region Type Extensions

        private static Dictionary<Type, Attribute> _getAttributeLookup = null;
        public static TAttribute GetAttribute<TAttribute>(this Type type) where TAttribute : Attribute
        {
            if (_getAttributeLookup == null)
            {
                _getAttributeLookup = new Dictionary<Type, Attribute>();
            }

            if (!_getAttributeLookup.Keys.Contains(type))
            {
                TAttribute attr = type.GetCustomAttributes(typeof(TAttribute), false).FirstOrDefault() as TAttribute;
                if (attr == null)
                    throw new Exception();
                _getAttributeLookup[type] = type.GetCustomAttributes(typeof(TAttribute), false).FirstOrDefault() as TAttribute;
            }

            return _getAttributeLookup[type] as TAttribute;
        }

        public static Boolean IsNullable(this Type baseType)
        {
            return baseType.IsGenericType && (baseType.GetGenericTypeDefinition() == typeof(Nullable<>));
        }

        public static String SafeToString(this Object content)
        {
            if (content == null)
                return String.Empty;

            return content.ToString();
        }

        public static String GetFriendlyTypeName(this Type type)
        {
            StringBuilder sb = new StringBuilder();

            if (type.IsGenericType)
            {
                Type genericType = type.GetGenericTypeDefinition();

                sb.AppendFormat("{0}.{1}<", genericType.Namespace, genericType.Name);

                Boolean first = true;

                foreach (Type typeArgument in type.GetGenericArguments())
                {
                    if (!first)
                        sb.Append(",");

                    GetFriendlyTypeName(typeArgument, sb);

                    first = false;
                }

                sb.Append(">");
            }
            else
                sb.AppendFormat("{0}.{1}", type.Namespace, type.Name);

            return sb.ToString();
        }

        private static void GetFriendlyTypeName(Type type, StringBuilder sb)
        {
            if (type.IsGenericType)
            {
                Type genericType = type.GetGenericTypeDefinition();

                sb.AppendFormat("{0}.{1}<", genericType.Namespace, genericType.Name);

                Boolean first = true;

                foreach (Type typeArgument in type.GetGenericArguments())
                {
                    if (!first)
                        sb.Append(",");

                    GetFriendlyTypeName(typeArgument, sb);

                    first = false;
                }

                sb.Append(">");
            }
            else
                sb.AppendFormat("{0}.{1}", type.Namespace, type.Name);
        }

        #endregion

        #region Enum Extensions

        /// <summary>
        /// Return the friendly description of an enum value, if it has been decorated with the DescriptionAttribute,
        /// otherwise, return the internal string representation of the enum value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static String ToDescription(this Enum value)
        {
            if (value == null)
                return String.Empty;

            String description = value.ToString();

            DescriptionAttribute[] attributes = (DescriptionAttribute[])value.GetType()
                .GetField(description)
                .GetCustomAttributes(typeof(DescriptionAttribute), false);

            return attributes.Length > 0 ? attributes[0].Description : description;
        }

        public static Int16? ToInt16(this Enum input)
        {
            if (input == null)
                return null;

            return (Int16)(Object)input;
        }

        public static Int16 ToInt16(this Enum input, Int16 @default)
        {
            return input.ToInt16() ?? @default;
        }

        public static Int32? ToInt32(this Enum input)
        {
            if (input == null)
                return null;

            return (Int32)(Object)input;
        }

        public static Int32 ToInt32(this Enum input, Int32 @default)
        {
            return input.ToInt32() ?? @default;
        }

        public static Int64? ToInt64(this Enum input)
        {
            if (input == null)
                return null;

            return (Int64)(Object)input;
        }

        public static Int64 ToInt64(this Enum input, Int64 @default)
        {
            return input.ToInt64() ?? @default;
        }

        #endregion

        #region File.IO Extensions

        public static String FilePathToUri(this String path)
        {
            return path.Replace("\\", "/").Replace("./", "");
        }

        public static String GetRelativePath(this DirectoryInfo sourcePath, DirectoryInfo destinationPath)
        {
            StringBuilder path = new StringBuilder(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_DIRECTORY,
                destinationPath.FullName,
                FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        public static String GetRelativePath(this DirectoryInfo sourcePath, FileInfo destinationPath)
        {
            StringBuilder path = new StringBuilder(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_DIRECTORY,
                destinationPath.FullName,
                FILE_ATTRIBUTE_NORMAL) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        public static String GetRelativePath(this FileInfo sourcePath, DirectoryInfo destinationPath)
        {
            StringBuilder path = new StringBuilder(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_NORMAL,
                destinationPath.FullName,
                FILE_ATTRIBUTE_DIRECTORY) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        public static String GetRelativePath(this FileInfo sourcePath, FileInfo destinationPath)
        {
            StringBuilder path = new StringBuilder(260);

            if (PathRelativePathTo(
                path,
                sourcePath.FullName,
                FILE_ATTRIBUTE_NORMAL,
                destinationPath.FullName,
                FILE_ATTRIBUTE_NORMAL) == 0)
                throw new ArgumentException("Paths must have a common prefix");

            return path.ToString();
        }

        private const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const int FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("shlwapi.dll", SetLastError = true)]
        private static extern int PathRelativePathTo(StringBuilder pszPath,
            string pszFrom, int dwAttrFrom, string pszTo, int dwAttrTo);

        #endregion

        #region Dictionary Extensions

        /// <summary>
        /// Attempts to convert any array of objects into a dictionary of objects.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Dictionary<Object, Object> ToDictionary(this Object[] input)
        {
            Int32 i = 0;
            Dictionary<Object, Object> buffer = new();

            if (input == null)
                return buffer;

            while (i < input.Length - 1)
                buffer[input[i++]] = input[i++];

            return buffer;
        }

        /// <summary>
        /// Converts an array of Object[] to a key/value pair dictionary.
        /// Duplicate keys will throw an exception.
        /// The last argument will be ignored if an odd number of arguments is supplied.
        /// </summary>
        /// <typeparam name="TKey">The type of the key parameters.</typeparam>
        /// <typeparam name="TValue">The type of the value parameters.</typeparam>
        /// <param name="args">The list of arguments to convert to a key/value pair dictionary</param>
        /// <returns>Returns a key/value pair dictionary</returns>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this Object[] args)
        {
            Int32 i = 0;
            Dictionary<TKey, TValue> buffer = new();

            if (args == null)
                return buffer;

            while (i < args.Length - 1)
                if ((args[i] is TKey) && (args[i + 1] is TValue))
                    buffer[(TKey)args[i++]] = (TValue)args[i++];
                else
                    i += 2;

            return buffer;
        }

        public static Dictionary<String, TValue> AddPrefix<TValue>(this Dictionary<String, TValue> input, String prefix)
        {
            return (from kvp in input
                    select new KeyValuePair<String, TValue>(String.Format("{0}{1}", prefix, kvp.Key), kvp.Value)).ToDictionary(k => k.Key, v => v.Value);
        }

        public static Dictionary<String, TValue> RemovePrefix<TValue>(this Dictionary<String, TValue> input, String prefix)
        {
            return (from kvp in input
                    where kvp.Key.StartsWith(prefix)
                    select new KeyValuePair<String, TValue>(kvp.Key.Substring(prefix.Length), kvp.Value)).ToDictionary(k => k.Key, v => v.Value);
        }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value)
        {
            input[key] = value;

            return input;
        }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, Dictionary<TKey, TValue> collection)
        {
            foreach (KeyValuePair<TKey, TValue> kvp in collection)
                input[kvp.Key] = kvp.Value;

            return input;
        }

        public static Dictionary<TKey, TValue> Prepend<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value)
        {
            Dictionary<TKey, TValue> buffer = new Dictionary<TKey, TValue> { { key, value } };

            foreach (KeyValuePair<TKey, TValue> kvp in input)
                buffer[kvp.Key] = kvp.Value;

            return buffer;
        }

        public static TValue GetValue<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue @default)
        {
            TValue result;

            if (input.TryGetValue(key, out result))
                return result;

            return @default;
        }

        #endregion

        #region Stream Extensions

        /// <summary>
        /// Read a Length-prefixed string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="byteLength">Size of the Length representation</param>
        /// <returns></returns>
        public static String ReadPString(this BinaryReader binaryReader, StringSizeEnum byteLength = StringSizeEnum.Int32)
        {
            Int32 stringLength = 0;

            switch (byteLength)
            {
                case StringSizeEnum.Int8:
                    stringLength = binaryReader.ReadByte();
                    break;
                case StringSizeEnum.Int16:
                    stringLength = binaryReader.ReadInt16();
                    break;
                case StringSizeEnum.Int32:
                    stringLength = binaryReader.ReadInt32();
                    break;
                default:
                    throw new NotSupportedException("Only Int8, Int16, and Int32 string sizes are supported");
            }

            // If there is actually a string to read
            if (stringLength > 0)
            {
                return new String(binaryReader.ReadChars(stringLength));
            }

            return null;
        }

        /// <summary>
        /// Read a NULL-Terminated string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <returns></returns>
        public static String ReadCString(this BinaryReader binaryReader)
        {
            var chars = new List<byte>();
            byte b = 255;
            while (b != 0 && binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
            {
                b = binaryReader.ReadByte();
                if (b != 0)
                {
                    chars.Add(b);
                }
            }
            return chars.Count > 0 ? new String(Encoding.UTF8.GetChars(chars.ToArray())) : null;
        }

        /// <summary>
        /// Read a Fixed-Length string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="stringLength">Size of the String</param>
        /// <returns></returns>
        public static String ReadFString(this BinaryReader binaryReader, Int32 stringLength)
        {
            Char[] chars = binaryReader.ReadChars(stringLength);

            for (Int32 i = 0; i < stringLength; i++)
            {
                if (chars[i] == 0)
                {
                    return new String(chars, 0, i);
                }
            }

            return new String(chars);
        }

        public static Byte[] ReadAllBytes(this Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Int64 oldPosition = stream.Position;
                stream.Position = 0;
                stream.CopyTo(ms);
                stream.Position = oldPosition;
                return ms.ToArray();
            }
        }

        #endregion

        #region Parser Extensions

        /// <summary>
        /// Converts a string into a target type.
        /// </summary>
        /// <param name="type">The type we wish to return.</param>
        /// <param name="input">The string value we wish to convert.</param>
        /// <returns>An object containing the data represented by the input string, in the input type.</returns>
        public static Object Parse(this String input, Type type, Object @default = null)
        {
            // Early exit for Strings
            if (type == typeof(String))
                return input;

            Boolean isNullable = false;

            // Add support for Nullable types
            if (type.IsNullable())
            {
                if (String.IsNullOrWhiteSpace(input))
                    return null;

                type = type.GetGenericArguments()[0];

                isNullable = true;
            }

            if (input == null)
                return @default;

            try
            {
                // Add some extra datetime support
                if (type == typeof(DateTime))
                {
                    DateTime result = DateTime.MinValue;

                    if (DateTime.TryParseExact(input, Utilities.DateTimeFormats, null, DateTimeStyles.None, out result))
                        return result;
                    else
                        return DateTime.Parse(input);
                }

                // If it's an enumeration, we'll try this.
                if (type.IsEnum)
                    return Enum.Parse(type, input);

                MethodInfo parseMethod;

                // Throw in a little static reflection caching
                if (!DDRIT._parserMap.TryGetValue(type, out parseMethod))
                {
                    // Attempt to find inbuilt parsing methods
                    parseMethod = type.GetMethod("Parse", new Type[] { typeof(String) });

                    DDRIT._parserMap[type] = parseMethod;
                }

                // If there's inbuilt parsing methods, attempt to use those.
                if (parseMethod != null)
                    return parseMethod.Invoke(null, new Object[] { input });
            }
            catch (Exception ex)
            {
                if (!isNullable && (@default == null))
                    throw;

                return @default;
            }

            // And if we weren't clever enough to cast them ourselves, we'll die gracefully and let people know WTF happened.
            throw new NotImplementedException(String.Format("Unable to parse `{0}` types", type.FullName));
        }

        public static TResult To<TResult>(this String input, TResult @default = default(TResult))
        {
            Type type = typeof(TResult);

            return (TResult)DDRIT.Parse(input, type, default(TResult));
        }

        public static Boolean ToBoolean(this String input, Boolean @default)
        {
            return input.ToBoolean() ?? @default;
        }

        public static Boolean? ToBoolean(this String input)
        {
            Boolean result = false;

            if (Boolean.TryParse(input, out result))
                return result;

            return null;
        }

        public static TEnum? ToEnum<TEnum>(this String input, Boolean ignoreCase = true) where TEnum : struct
        {
            if (!typeof(TEnum).IsEnum) throw new InvalidOperationException("Generic type argument is not a System.Enum");

            TEnum buffer;

            if (Enum.TryParse<TEnum>(input, ignoreCase, out buffer))
                return buffer;

            return null;
        }

        public static TResult ToEnum<TResult>(this String input, TResult @default, Boolean ignoreCase = true) where TResult : struct
        {
            return input.ToEnum<TResult>(ignoreCase) ?? @default;
        }

        public static Int16? ToInt16(this String input)
        {
            Int16 result = 0;

            if (Int16.TryParse(input, out result))
                return result;

            return null;
        }

        public static Int16 ToInt16(this String input, Int16 @default)
        {
            return input.ToInt16() ?? @default;
        }

        public static Int32? ToInt32(this String input)
        {
            Int32 result = 0;

            if (Int32.TryParse(input, out result))
                return result;

            return null;
        }

        public static Int32 ToInt32(this String input, Int32 @default)
        {
            return input.ToInt32() ?? @default;
        }

        public static Int64? ToInt64(this String input)
        {
            Int64 result = 0;

            if (Int64.TryParse(input, out result))
                return result;

            return null;
        }

        public static Int64 ToInt64(this String input, Int64 @default)
        {
            return input.ToInt64() ?? @default;
        }

        public static Single? ToSingle(this String input)
        {
            Single result = 0;

            if (Single.TryParse(input, out result))
                return result;

            return null;
        }

        public static Single ToSingle(this String input, Single @default)
        {
            return input.ToSingle() ?? @default;
        }

        public static Double? ToDouble(this String input)
        {
            Double result = 0;

            if (Double.TryParse(input, out result))
                return result;

            return null;
        }

        public static Double ToDouble(this String input, Double @default)
        {
            return input.ToDouble() ?? @default;
        }

        public static Decimal? ToDecimal(this String input)
        {
            Decimal result = 0;

            if (Decimal.TryParse(input, out result))
                return result;

            return null;
        }

        public static Decimal ToDecimal(this String input, Decimal @default)
        {
            return input.ToDecimal() ?? @default;
        }

        public static DateTime? ToDateTime(this String input)
        {
            DateTime result = DateTime.MinValue;

            if (DateTime.TryParseExact(input, Utilities.DateTimeFormats, null, DateTimeStyles.None, out result))
                return result;

            if (DateTime.TryParse(input, out result))
                return result;

            return null;
        }

        public static DateTime ToDateTime(this String input, DateTime @default)
        {
            return input.ToDateTime() ?? @default;
        }

        public static DateTimeOffset? ToDateTimeOffset(this String input)
        {
            DateTimeOffset result = DateTimeOffset.MinValue;

            if (DateTimeOffset.TryParseExact(input, Utilities.DateTimeFormats, null, DateTimeStyles.None, out result))
                return result;

            if (DateTimeOffset.TryParse(input, out result))
                return result;

            return null;
        }

        public static DateTimeOffset ToDateTimeOffset(this String input, DateTimeOffset @default)
        {
            return input.ToDateTime() ?? @default;
        }

        public static DateTimeOffset? ToDateTimeOffset(this DateTime? input)
        {
            if (input == null)
                return null;

            return new DateTimeOffset(input.Value);
        }

        public static DateTimeOffset ToDateTimeOffset(this DateTime input) { return new DateTimeOffset(input); }

        public static TimeSpan? ToTimeSpan(this String input)
        {
            TimeSpan result = TimeSpan.MinValue;

            if (TimeSpan.TryParse(input, out result))
                return result;

            return null;
        }

        public static TimeSpan ToTimeSpan(this String input, TimeSpan @default)
        {
            return input.ToTimeSpan() ?? @default;
        }

        #endregion

        #region String Extensions

        public static String StripTags(this String input)
        {
            Char[] array = new Char[input.Length];
            Int32 arrayIndex = 0;
            Boolean inside = false;
            Char let;

            for (Int32 i = 0; i < input.Length; i++)
            {
                let = input[i];

                if (let == '<')
                {
                    inside = true;
                    continue;
                }
                if (let == '>')
                {
                    inside = false;
                    continue;
                }
                if (!inside)
                {
                    array[arrayIndex] = let;
                    arrayIndex++;
                }
            }

            return new String(array, 0, arrayIndex);
        }

        public static String TrimTo(this String input, Int32 maxLength, Boolean stripTags = true)
        {
            if (!String.IsNullOrWhiteSpace(input))
            {
                String cleanString = input.Replace(@"&nbsp;", " ").Replace("  ", " ");

                if (stripTags)
                    cleanString = cleanString.StripTags();

                if (cleanString.Length > 100)
                {
                    String[] parts = cleanString.Split(' ');
                    StringBuilder sb = new StringBuilder();
                    Int32 i = 0;
                    Int32 j = parts.Length;

                    while (i < j && (sb.Length + parts[i].Length < maxLength))
                    {
                        sb.Append(" ");
                        sb.Append(parts[i++]);
                    }

                    sb.Append("...");

                    return sb.ToString().Trim();
                }

                return cleanString;
            }

            return String.Empty;
        }

        #endregion

        #region Natural Language Extensions

        /// <summary>
        /// Converts an input integer to its ordinal number
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal</returns>
        public static String ToOrdinal(this Int32 input)
        {
            return input + ToOrdinalSuffix(input);
        }

        /// <summary>
        /// Converts an input integer to its ordinal suffix
        /// Useful if you need to format the suffix separately of the number itself
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal suffix</returns>
        public static String ToOrdinalSuffix(this Int32 input)
        {
            //TODO: this only handles English ordinals - in future we may wish to consider the culture

            //note, we are allowing zeroth - http://en.wikipedia.org/wiki/Zeroth
            if (input < 0)
                throw new ArgumentOutOfRangeException("input", "Ordinal numbers cannot be negative");

            //first check special case, if the result ends in 11, 12, 13, should be th
            switch (input % 100)
            {
                case 11:
                case 12:
                case 13:
                    return "th";
            }

            //else we just check the last digit
            switch (input % 10)
            {
                case 1:
                    return ("st");
                case 2:
                    return ("nd");
                case 3:
                    return ("rd");
                default:
                    return ("th");
            }
        }

        #endregion

        #region Cryptography Extensions

        /// <summary>
        /// From http://stackoverflow.com/questions/311165/how-do-you-convert-byte-array-to-hexadecimal-string-and-vice-versa/14333437#14333437
        /// </summary>
        /// <param name="bytes">Array of bytes to convert to hex string</param>
        /// <returns>A hex string representation of the input bytes</returns>
        public static String ToHex(this Byte[] bytes)
        {
            Char[] buffer = new Char[bytes.Length * 2];
            Int32 b;
            for (Int32 i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                buffer[i * 2] = (Char)(55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                buffer[i * 2 + 1] = (Char)(55 + b + (((b - 10) >> 31) & -7));
            }

            return new String(buffer);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An MD5 hash of the given string</returns>
        public static String ToMD5(this String input)
        {
            MD5 md5 = MD5.Create();
            Byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            Byte[] hash = md5.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA1 hash of the given string</returns>
        public static String ToSHA1(this String input)
        {
            SHA1 sha = SHA1.Create();
            Byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            Byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA256 hash of the given string</returns>
        public static String ToSHA256(this String input)
        {
            SHA256 sha = SHA256.Create();
            Byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            Byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA384 hash of the given string</returns>
        public static String ToSHA384(this String input)
        {
            SHA384 sha = SHA384.Create();
            Byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            Byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA512 hash of the given string</returns>
        public static String ToSHA512(this String input)
        {
            SHA512 sha = SHA512.Create();
            Byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            Byte[] hash = sha.ComputeHash(inputBytes);

            return hash.ToHex(); // BitConverter.ToString(hash);
        }

        #endregion

        #region Enumerable Extensions

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, TSource @default)
        {
            return source.Count() > 0 ? source.First() : @default;
        }

        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, Boolean> predicate, TSource @default)
        {
            source = source.Where(predicate);
            return source.Count() > 0 ? source.First() : @default;
        }

        #endregion

        #region DateTime Extensions

        public static DateTime StartOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return date.Date.AddDays(-(Int32)date.AddDays(-(Int32)startOfWeek).DayOfWeek); }

        public static DateTime EndOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return date.StartOfWeek(startOfWeek).AddDays(7); }

        public static DateTime TrimTo(this DateTime datetime, TimeSpan interval)
        {
            if (interval.TotalHours > 24)
                throw new ArgumentOutOfRangeException("Timespan too large, timespan must be less than 24 hours");

            return datetime.AddMilliseconds(0 - (datetime.TimeOfDay.TotalMilliseconds % interval.TotalMilliseconds));
        }

        public static DateTimeOffset TrimTo(this DateTimeOffset datetime, TimeSpan interval)
        {
            if (interval.TotalHours > 24)
                throw new ArgumentOutOfRangeException("Timespan too large, timespan must be less than 24 hours");

            return datetime.AddMilliseconds(0 - (datetime.TimeOfDay.TotalMilliseconds % interval.TotalMilliseconds));
        }

        public static TimeSpan TrimTo(this TimeSpan timespan, TimeSpan interval)
        {
            return TimeSpan.FromMilliseconds(timespan.TotalMilliseconds - (timespan.TotalMilliseconds % interval.TotalMilliseconds));
        }

        #endregion
    }
}

#region Namespace Proxies

namespace System
{
    public static class _Proxy
    {
        /// <summary>
        /// Attempt to serialize any object into a XML string.
        /// </summary>
        /// <param name="input">The object to serialize.</param>
        /// <returns>A string containing the serialized object.</returns>
        public static String ToXML(this Object input) { return DDRIT.ToXML(input); }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <param name="type">The type to convert to.</param>
        /// <returns>An Object deserialized from the string.</returns>
        public static Object FromXML(this String input, Type type) { return DDRIT.FromXML(input, type); }

        /// <summary>
        /// Attempt to deserialize any XML string into an object.
        /// </summary>
        /// <typeparam name="TResult">The type to convert to.</typeparam>
        /// <param name="input">The string to deserialize to an object.</param>
        /// <returns>An Object of type T deserialized from the string.</returns>
        public static TResult FromXML<TResult>(this String input) { return DDRIT.FromXML<TResult>(input); }

        public static String ToHex(this Byte[] bytes) { return DDRIT.ToHex(bytes); }

        /// <summary>
        /// Strip HTML/XML tags from any string.
        /// </summary>
        /// <param name="input">HTML content.</param>
        /// <returns>Content free of HTML tags.</returns>
        public static String StripTags(this String input) { return DDRIT.StripTags(input); }

        /// <summary>
        /// Trim markup to a particular length, and append an elipsis if there is truncated content
        /// </summary>
        /// <param name="input">The content to trim</param>
        /// <param name="maxLength">The maximum length of the returned content</param>
        /// <param name="stripTags">(true)Remove HTML tags before trimming</param>
        /// <returns></returns>
        public static String TrimTo(this String input, Int32 maxLength, Boolean stripTags = true) { return DDRIT.TrimTo(input, maxLength, stripTags); }

        /// <summary>
        /// Trim a DateTime to the smallest whole TimeSpan that fits within a 24 hour period
        /// </summary>
        /// <param name="input">The time to trim</param>
        /// <param name="interval">The smallest timespan per day to allow</param>
        /// <returns></returns>
        public static DateTime TrimTo(this DateTime input, TimeSpan interval) { return DDRIT.TrimTo(input, interval); }

        /// <summary>
        /// Trim a DateTime to the smallest whole TimeSpan that fits within a 24 hour period
        /// </summary>
        /// <param name="input">The time to trim</param>
        /// <param name="interval">The smallest timespan per day to allow</param>
        /// <returns></returns>
        public static DateTimeOffset TrimTo(this DateTimeOffset input, TimeSpan interval) { return DDRIT.TrimTo(input, interval); }

        /// <summary>
        /// Trim a TimeSpan to the smallest whole TimeSpan that fits within a 24 hour period
        /// </summary>
        /// <param name="input">The time to trim</param>
        /// <param name="interval">The smallest timespan per day to allow</param>
        /// <returns></returns>
        public static TimeSpan TrimTo(this TimeSpan input, TimeSpan interval) { return DDRIT.TrimTo(input, interval); }

        public static Boolean IsNullable(this Type baseType) { return DDRIT.IsNullable(baseType); }

        public static TAttribute GetAttribute<TAttribute>(this Type type) where TAttribute : Attribute { return DDRIT.GetAttribute<TAttribute>(type); }

        public static String GetFriendlyTypeName(this Type type) { return DDRIT.GetFriendlyTypeName(type); }

        /// <summary>
        /// Return the friendly description of an enum value, if it has been decorated with the DescriptionAttribute,
        /// otherwise, return the internal string representation of the enum value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static String ToDescription(this Enum value) { return DDRIT.ToDescription(value); }

        public static String FilePathToUri(this String path) { return DDRIT.FilePathToUri(path); }

        public static DateTime StartOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return DDRIT.StartOfWeek(date, startOfWeek); }

        public static DateTime EndOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday) { return DDRIT.EndOfWeek(date, startOfWeek); }

        /// <summary>
        /// Attempts to convert any array of objects into a dictionary of objects.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Dictionary<Object, Object> ToDictionary(this Object[] input) { return DDRIT.ToDictionary(input); }

        /// <summary>
        /// Converts an array of Object[] to a key/value pair dictionary.
        /// Duplicate keys will throw an exception.
        /// The last argument will be ignored if an odd number of arguments is supplied.
        /// </summary>
        /// <typeparam name="TKey">The type of the key parameters.</typeparam>
        /// <typeparam name="TValue">The type of the value parameters.</typeparam>
        /// <param name="args">The list of arguments to convert to a key/value pair dictionary</param>
        /// <returns>Returns a key/value pair dictionary</returns>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this Object[] args) { return DDRIT.ToDictionary<TKey, TValue>(args); }

        /// <summary>
        /// Read a Length-prefixed string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="byteLength">Size of the Length representation</param>
        /// <returns></returns>
        public static String ReadPString(this BinaryReader binaryReader, StringSizeEnum byteLength = StringSizeEnum.Int32) { return DDRIT.ReadPString(binaryReader, byteLength); }

        /// <summary>
        /// Read a NULL-Terminated string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <returns></returns>
        public static String ReadCString(this BinaryReader binaryReader) { return DDRIT.ReadCString(binaryReader); }

        /// <summary>
        /// Read a Fixed-Length string from the stream
        /// </summary>
        /// <param name="binaryReader"></param>
        /// <param name="stringLength">Size of the String</param>
        /// <returns></returns>
        public static String ReadFString(this BinaryReader binaryReader, Int32 stringLength) { return DDRIT.ReadFString(binaryReader, stringLength); }

        public static Byte[] ReadAllBytes(this Stream stream) { return DDRIT.ReadAllBytes(stream); }

        /// <summary>
        /// Converts a string into a target type.
        /// </summary>
        /// <param name="type">The type we wish to return.</param>
        /// <param name="input">The string value we wish to convert.</param>
        /// <returns>An object containing the data represented by the input string, in the input type.</returns>
        public static Object Parse(this String input, Type type, Object @default = null) { return DDRIT.Parse(input, type, @default); }

        public static TResult To<TResult>(this String input, TResult @default = default(TResult)) { return DDRIT.To<TResult>(input, @default); }

        public static Boolean? ToBoolean(this String input) { return DDRIT.ToBoolean(input); }

        public static Boolean ToBoolean(this String input, Boolean @default) { return DDRIT.ToBoolean(input, @default); }

        public static Int16? ToInt16(this String input) { return DDRIT.ToInt16(input); }

        public static Int16 ToInt16(this String input, Int16 @default) { return DDRIT.ToInt16(input, @default); }

        public static Int32? ToInt32(this String input) { return DDRIT.ToInt32(input); }

        public static Int32 ToInt32(this String input, Int32 @default) { return DDRIT.ToInt32(input, @default); }

        public static Int64? ToInt64(this String input) { return DDRIT.ToInt64(input); }

        public static Int64 ToInt64(this String input, Int64 @default) { return DDRIT.ToInt64(input, @default); }

        public static Int16? ToInt16(this Enum input) { return DDRIT.ToInt16(input); }

        public static Int16 ToInt16(this Enum input, Int16 @default) { return DDRIT.ToInt16(input, @default); }

        public static Int32? ToInt32(this Enum input) { return DDRIT.ToInt32(input); }

        public static Int32 ToInt32(this Enum input, Int32 @default) { return DDRIT.ToInt32(input, @default); }

        public static Int64? ToInt64(this Enum input) { return DDRIT.ToInt64(input); }

        public static Int64 ToInt64(this Enum input, Int64 @default) { return DDRIT.ToInt64(input, @default); }

        public static TResult ToEnum<TResult>(this String input, TResult @default, Boolean ignoreCase = true) where TResult : struct { return DDRIT.ToEnum<TResult>(input, @default, ignoreCase); }

        public static TEnum? ToEnum<TEnum>(this String input, Boolean ignoreCase = true) where TEnum : struct { return DDRIT.ToEnum<TEnum>(input, ignoreCase); }

        public static Single? ToSingle(this String input) { return DDRIT.ToSingle(input); }

        public static Single ToSingle(this String input, Single @default) { return DDRIT.ToSingle(input, @default); }

        public static Double? ToDouble(this String input) { return DDRIT.ToDouble(input); }

        public static Double ToDouble(this String input, Double @default) { return DDRIT.ToDouble(input, @default); }

        public static Decimal? ToDecimal(this String input) { return DDRIT.ToDecimal(input); }

        public static Decimal ToDecimal(this String input, Decimal @default) { return DDRIT.ToDecimal(input, @default); }

        public static DateTime? ToDateTime(this String input) { return DDRIT.ToDateTime(input); }

        public static DateTime ToDateTime(this String input, DateTime @default) { return DDRIT.ToDateTime(input, @default); }

        public static DateTimeOffset? ToDateTimeOffset(this DateTime? input) { return DDRIT.ToDateTimeOffset(input); }

        public static DateTimeOffset ToDateTimeOffset(this DateTime input) { return DDRIT.ToDateTimeOffset(input); }

        public static DateTimeOffset? ToDateTimeOffset(this String input) { return DDRIT.ToDateTimeOffset(input); }

        public static DateTimeOffset ToDateTimeOffset(this String input, DateTime @default) { return DDRIT.ToDateTimeOffset(input, @default); }

        public static TimeSpan? ToTimeSpan(this String input) { return DDRIT.ToTimeSpan(input); }

        public static TimeSpan ToTimeSpan(this String input, TimeSpan @default) { return DDRIT.ToTimeSpan(input, @default); }

        /// <summary>
        /// Converts an input integer to its ordinal number
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal</returns>
        public static String ToOrdinal(this Int32 input) { return DDRIT.ToOrdinal(input); }

        /// <summary>
        /// Converts an input integer to its ordinal suffix
        /// Useful if you need to format the suffix separately of the number itself
        /// </summary>
        /// <param name="input">The integer to convert</param>
        /// <returns>Returns a string of the ordinal suffix</returns>
        public static String ToOrdinalSuffix(this Int32 input) { return DDRIT.ToOrdinalSuffix(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An MD5 hash of the given string</returns>
        public static String ToMD5(this String input) { return DDRIT.ToMD5(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA1 hash of the given string</returns>
        public static String ToSHA1(this String input) { return DDRIT.ToSHA1(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA256 hash of the given string</returns>
        public static String ToSHA256(this String input) { return DDRIT.ToSHA256(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA384 hash of the given string</returns>
        public static String ToSHA384(this String input) { return DDRIT.ToSHA384(input); }

        /// <summary>
        /// Return the hash of a string value
        /// </summary>
        /// <param name="input">The string to hash</param>
        /// <returns>An SHA512 hash of the given string</returns>
        public static String ToSHA512(this String input) { return DDRIT.ToSHA512(input); }
    }
}

namespace System.Linq
{
    public static class _Proxy
    {
        //
        // Summary:
        //     Returns the first element of a sequence, or a default value if the sequence
        //     contains no elements.
        //
        // Parameters:
        //   source:
        //     The System.Collections.Generic.IEnumerable<T> to return the first element
        //     of.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        // Returns:
        //     default(TSource) if source is empty; otherwise, the first element in source.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source is null.
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, TSource @default)
        {
            return DDRIT.FirstOrDefault(source, @default);
        }

        //
        // Summary:
        //     Returns the first element of the sequence that satisfies a condition or a
        //     default value if no such element is found.
        //
        // Parameters:
        //   source:
        //     An System.Collections.Generic.IEnumerable<T> to return an element from.
        //
        //   predicate:
        //     A function to test each element for a condition.
        //
        // Type parameters:
        //   TSource:
        //     The type of the elements of source.
        //
        // Returns:
        //     default(TSource) if source is empty or if no element passes the test specified
        //     by predicate; otherwise, the first element in source that passes the test
        //     specified by predicate.
        //
        // Exceptions:
        //   System.ArgumentNullException:
        //     source or predicate is null.
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, Boolean> predicate, TSource @default)
        {
            return DDRIT.FirstOrDefault(source, predicate, @default);
        }
    }
}

namespace System.IO
{
    public static class _Proxy
    {
        public static String GetRelativePath(this DirectoryInfo sourcePath, DirectoryInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }

        public static String GetRelativePath(this DirectoryInfo sourcePath, FileInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }

        public static String GetRelativePath(this FileInfo sourcePath, DirectoryInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }

        public static String GetRelativePath(this FileInfo sourcePath, FileInfo destinationPath) { return DDRIT.GetRelativePath(sourcePath, destinationPath); }
    }
}

namespace System.Collections.Generic
{
    public static class _Proxy
    {
        public static Dictionary<String, TValue> AddPrefix<TValue>(this Dictionary<String, TValue> input, String prefix) { return DDRIT.AddPrefix<TValue>(input, prefix); }

        public static Dictionary<String, TValue> RemovePrefix<TValue>(this Dictionary<String, TValue> input, String prefix) { return DDRIT.RemovePrefix<TValue>(input, prefix); }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value) { return DDRIT.Append<TKey, TValue>(input, key, value); }

        public static Dictionary<TKey, TValue> Append<TKey, TValue>(this Dictionary<TKey, TValue> input, Dictionary<TKey, TValue> collection) { return DDRIT.Append<TKey, TValue>(input, collection); }

        public static Dictionary<TKey, TValue> Prepend<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue value) { return DDRIT.Prepend<TKey, TValue>(input, key, value); }

        public static TValue GetValue<TKey, TValue>(this Dictionary<TKey, TValue> input, TKey key, TValue @default) { return DDRIT.GetValue<TKey, TValue>(input, key, @default); }
    }
}

#endregion

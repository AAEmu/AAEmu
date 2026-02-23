using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Extensions;

namespace CgfConverter.CryXmlB;

public static class CryXmlSerializer
{
    public static readonly ImmutableArray<byte> PbxmlMagic = "pbxml\0"u8.ToArray().ToImmutableArray();
    public static readonly ImmutableArray<byte> CryXmlMagic = "CryXmlB\0"u8.ToArray().ToImmutableArray();

    public static XmlDocument ReadFile(string inFile, bool writeLog = false)
        => ReadStream(File.OpenRead(inFile), writeLog);

    public static XmlDocument ReadStream(Stream inStream, bool writeLog = false, bool leaveOpen = false)
    {
        Span<byte> peek = stackalloc byte[Math.Max(PbxmlMagic.Length, CryXmlMagic.Length)];
        try
        {
            // Ensure that inStream is peekable.
            // If it isn't, then make a copy of the stream into a MemoryStream.
            if (!inStream.CanSeek)
            {
                var prevStream = inStream;
                inStream = new MemoryStream();
                prevStream.CopyTo(inStream);
                if (!leaveOpen)
                    prevStream.Dispose();
            }
            
            peek = peek[..inStream.Read(peek)];
            inStream.Position -= peek.Length;
            
            // There is no way that a text XML file starts with these bytes.
            if (peek.StartsWith(PbxmlMagic.AsSpan()))
                return LoadPbxmlFile(new(inStream));
            if (peek.StartsWith(CryXmlMagic.AsSpan()))
                return LoadScXmlFile(writeLog, new(inStream));

            // Attempt parsing as text XML document as the final option,
            // as the file may be containing byte order mark, or is in other unicode encodings else than UTF-8.
            var xml = new XmlDocument();
            xml.Load(inStream);
            return xml;
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"Unknown file format(head={peek.ToString()})", e);
        }
        finally
        {
            if (!leaveOpen)
                inStream.Dispose();
        }
    }

    private static XmlDocument LoadPbxmlFile(BinaryReader br)
    {
        if (!PbxmlMagic.AsSpan().SequenceEqual(br.ReadBytes(PbxmlMagic.Length)))
            throw new InvalidDataException("Provided stream does not contain a pbxml file.");

        XmlDocument doc = new();

        var element = CreateNewElement(br, doc);
        doc.AppendChild(element);
        return doc;
    }

    private static XmlElement CreateNewElement(BinaryReader br, XmlDocument doc)
    {
        var numberOfChildren = br.ReadCryInt();
        var numberOfAttributes = br.ReadCryInt();

        var nodeName = br.ReadCString();

        var element = doc.CreateElement(nodeName);

        for (var i = 0; i < numberOfAttributes; i++)
        {
            var key = br.ReadCString();
            var value = br.ReadCString();
            element.SetAttribute(key, value);
        }

        var nodeText = br.ReadCString();
        if (nodeText != "")
            element.AppendChild(doc.CreateTextNode(nodeText));

        for (var i = 0; i < numberOfChildren; i++)
        {
            var expectedLength = br.ReadCryInt();
            var expectedPosition = br.BaseStream.Position + expectedLength;
            element.AppendChild(CreateNewElement(br, doc));
            if (i + 1 == numberOfChildren)
            {
                if (expectedLength != 0)
                    throw new InvalidDataException("Last child node must not have an expectedLength.");
            }
            else
            {
                if (br.BaseStream.Position != expectedPosition)
                    throw new InvalidDataException("Expected length does not match.");
            }
        }

        return element;
    }

    private static XmlDocument LoadScXmlFile(bool writeLog, BinaryReader br)
    {
        if (!CryXmlMagic.AsSpan().SequenceEqual(br.ReadBytes(CryXmlMagic.Length)))
            throw new InvalidDataException("Provided stream does not contain a CryXml file.");

        var headerLength = br.BaseStream.Position;
        var fileLength = br.ReadInt32();

        var nodeTableOffset = br.ReadInt32();
        var nodeTableCount = br.ReadInt32();
        var nodeTableSize = 28;

        var referenceTableOffset = br.ReadInt32();
        var referenceTableCount = br.ReadInt32();
        var referenceTableSize = 8;

        var orderTableOffset = br.ReadInt32();
        var orderTableCount = br.ReadInt32();
        var orderTableLength = 4;

        var contentOffset = br.ReadInt32();
        var contentLength = br.ReadInt32();

        if (writeLog)
        {
            Console.WriteLine("Header");
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x00, fileLength);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x04, nodeTableOffset);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x08, nodeTableCount);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x12, referenceTableOffset);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x16, referenceTableCount);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x20, orderTableOffset);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x24, orderTableCount);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x28, contentOffset);
            Console.WriteLine("0x{0:X6}: {1:X8} (Dec: {1:D8})", headerLength + 0x32, contentLength);
            Console.WriteLine("");
            Console.WriteLine("Node Table");
        }

        var nodeTable = new List<CryXmlNode>();
        br.BaseStream.Seek(nodeTableOffset, SeekOrigin.Begin);
        int nodeID = 0;

        while (br.BaseStream.Position < nodeTableOffset + nodeTableCount * nodeTableSize)
        {
            var position = br.BaseStream.Position;
            var value = new CryXmlNode
            {
                NodeID = nodeID++,
                NodeNameOffset = br.ReadInt32(),
                ItemType = br.ReadInt32(),
                AttributeCount = br.ReadInt16(),
                ChildCount = br.ReadInt16(),
                ParentNodeID = br.ReadInt32(),
                FirstAttributeIndex = br.ReadInt32(),
                FirstChildIndex = br.ReadInt32(),
                Reserved = br.ReadInt32(),
            };

            nodeTable.Add(value);
            if (writeLog)
            {
                Console.WriteLine(
                    "0x{0:X6}: {1:X8} {2:X8} {3:X4} {4:X4} {5:X8} {6:X8} {7:X8} {8:X8}",
                    position,
                    value.NodeNameOffset,
                    value.ItemType,
                    value.AttributeCount,
                    value.ChildCount,
                    value.ParentNodeID,
                    value.FirstAttributeIndex,
                    value.FirstChildIndex,
                    value.Reserved);
            }
        }

        if (writeLog)
            Console.WriteLine("\nReference Table");

        var attributeTable = new List<CryXmlReference>();
        br.BaseStream.Seek(referenceTableOffset, SeekOrigin.Begin);

        while (br.BaseStream.Position < referenceTableOffset + referenceTableCount * referenceTableSize)
        {
            var position = br.BaseStream.Position;
            var value = new CryXmlReference
            {
                NameOffset = br.ReadInt32(),
                ValueOffset = br.ReadInt32()
            };

            attributeTable.Add(value);

            if (writeLog)
                Console.WriteLine("0x{0:X6}: {1:X8} {2:X8}", position, value.NameOffset, value.ValueOffset);
        }

        if (writeLog)
            Console.Write("\nOrder Table... ");

        var orderTable = new List<int>();
        br.BaseStream.Seek(orderTableOffset, SeekOrigin.Begin);
        while (br.BaseStream.Position < orderTableOffset + orderTableCount * orderTableLength)
        {
            var position = br.BaseStream.Position;
            var value = br.ReadInt32();

            orderTable.Add(value);

            if (writeLog)
                Console.WriteLine("0x{0:X6}: {1:X8}", position, value);
        }

        if (writeLog)
        {
            Console.WriteLine("{0} entries.", orderTable.Count);
            Console.WriteLine("\nDynamic Dictionary");
        }

        var dataTable = new List<CryXmlValue>();
        br.BaseStream.Seek(contentOffset, SeekOrigin.Begin);

        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            var position = br.BaseStream.Position;
            var value = new CryXmlValue
            {
                Offset = (int)position - contentOffset,
                Value = br.ReadCString(),
            };
            dataTable.Add(value);

            if (writeLog)
                Console.WriteLine("0x{0:X6}: {1:X8} {2}", position, value.Offset, value.Value);
        }

        var dataMap = dataTable.ToDictionary(k => k.Offset, v => v.Value);

        var attributeIndex = 0;

        var xmlDoc = new XmlDocument();

#pragma warning disable CS0219 // The variable 'bugged' is assigned but its value is never used
        var bugged = false;
#pragma warning restore CS0219 // The variable 'bugged' is assigned but its value is never used

        var xmlMap = new Dictionary<int, XmlElement>();

        foreach (var node in nodeTable)
        {
            XmlElement element = xmlDoc.CreateElement(dataMap[node.NodeNameOffset]);

            for (int i = 0, j = node.AttributeCount; i < j; i++)
            {
                if (dataMap.TryGetValue(attributeTable[attributeIndex].ValueOffset, out var attrValue))
                    element.SetAttribute(dataMap[attributeTable[attributeIndex].NameOffset], attrValue);
                else
                {
                    bugged = true;
                    element.SetAttribute(dataMap[attributeTable[attributeIndex].NameOffset], "BUGGED");
                }
                attributeIndex++;
            }

            xmlMap[node.NodeID] = element;
            if (xmlMap.TryGetValue(node.ParentNodeID, out var parent))
                parent.AppendChild(element);
            else
                xmlDoc.AppendChild(element);
        }
        
        if (writeLog && bugged)
            Console.WriteLine("XML file had attributes without valid value.");

        return xmlDoc;
    }

    public static TObject Deserialize<TObject>(Stream inStream, bool closeAfter = false) where TObject : class
    {
        try
        {
            using MemoryStream ms = new();
            var xs = new XmlSerializer(typeof(TObject));
            var xmlDoc = ReadStream(inStream);

            xmlDoc.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return (TObject) (xs.Deserialize(ms) ?? throw new NullReferenceException("Deserialize returned null"));
        }
        finally
        {
            if (closeAfter)
                inStream.Close();
        }
    }

    public static TObject Deserialize<TObject>(string inFile) where TObject : class
    {
        using MemoryStream ms = new MemoryStream();
        var xmlDoc = CryXmlSerializer.ReadFile(inFile);

        xmlDoc.Save(ms);

        ms.Seek(0, SeekOrigin.Begin);

        XmlSerializer xs = new XmlSerializer(typeof(TObject));

        return (TObject) (xs.Deserialize(ms) ?? throw new NullReferenceException("Deserialize returned null"));
    }
}

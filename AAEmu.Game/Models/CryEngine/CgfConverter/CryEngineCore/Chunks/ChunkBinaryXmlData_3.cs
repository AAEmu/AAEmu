using System;
using System.IO;
using System.Xml;
using CgfConverter.CryXmlB;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkBinaryXmlData_3 : ChunkBinaryXmlData     //  0xCCCBF004:  Binary XML Data
{
    public XmlDocument? Data { get; private set; }

    public override void Read(BinaryReader b)
    {
        base.Read(b);

        var bytesToRead = (Int32)(this.Size - Math.Max(b.BaseStream.Position - this.Offset, 0));

        var buffer = b.ReadBytes(bytesToRead);

        using (var memoryStream = new MemoryStream(buffer))
        {
            this.Data = CryXmlSerializer.ReadStream(memoryStream);
        }
    }
}

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CgfConverter.Services;


public class EndiannessChangeableBinaryReader : BinaryReader
{
    public EndiannessChangeableBinaryReader(Stream input) : base(input)
    {
    }

    public EndiannessChangeableBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
    {
    }

    public EndiannessChangeableBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
    {
    }

    public bool IsBigEndian { get; set; } = false;

    public override short ReadInt16() => IsBigEndian ? BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2)) : base.ReadInt16();
    public override ushort ReadUInt16() => IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2)) : base.ReadUInt16();
    public override int ReadInt32() => IsBigEndian ? BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4)) : base.ReadInt32();
    public override uint ReadUInt32() => IsBigEndian ? BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4)) : base.ReadUInt32();
    public override long ReadInt64() => IsBigEndian ? BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8)) : base.ReadInt64();
    public override ulong ReadUInt64() => IsBigEndian ? BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8)) : base.ReadUInt64();
    public override Half ReadHalf() => IsBigEndian ? BitConverter.Int16BitsToHalf(BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2))) : base.ReadHalf();
    public override float ReadSingle() => IsBigEndian ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4))) : base.ReadSingle();
    public override double ReadDouble() => IsBigEndian ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8))) : base.ReadDouble();
}
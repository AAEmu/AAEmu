using System.Buffers.Binary;
using System.Text;

namespace AAEmu.BillServer.Protocol;

/// <summary>Little-endian archive reader (values only; field names never on wire).</summary>
public sealed class BillReader
{
    private readonly byte[] _data;
    private int _o;

    public BillReader(byte[] data) => _data = data;

    public int Remaining => _data.Length - _o;

    public ushort ReadU16()
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_o));
        _o += 2;
        return v;
    }

    public short ReadI16()
    {
        var v = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_o));
        _o += 2;
        return v;
    }

    public int ReadI32()
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_o));
        _o += 4;
        return v;
    }

    public uint ReadU32()
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_o));
        _o += 4;
        return v;
    }

    public long ReadI64()
    {
        var v = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(_o));
        _o += 8;
        return v;
    }

    public ulong ReadU64()
    {
        var v = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_o));
        _o += 8;
        return v;
    }

    public byte ReadU8() => _data[_o++];

    public string ReadString()
    {
        var len = ReadU16();
        if (len == 0)
            return string.Empty;
        var s = Encoding.UTF8.GetString(_data, _o, len);
        _o += len;
        return s;
    }
}

/// <summary>Little-endian archive writer.</summary>
public sealed class BillWriter
{
    private readonly MemoryStream _ms = new();

    public void WriteU8(byte v) => _ms.WriteByte(v);

    public void WriteU16(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        _ms.Write(b);
    }

    public void WriteI16(short v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(b, v);
        _ms.Write(b);
    }

    public void WriteI32(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        _ms.Write(b);
    }

    public void WriteU32(uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        _ms.Write(b);
    }

    public void WriteI64(long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, v);
        _ms.Write(b);
    }

    public void WriteU64(ulong v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, v);
        _ms.Write(b);
    }

    public void WriteString(string? text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        if (bytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("string too long");
        WriteU16((ushort)bytes.Length);
        _ms.Write(bytes, 0, bytes.Length);
    }

    public byte[] ToArray() => _ms.ToArray();
}

public static class BillFrame
{
    /// <summary>frame = [u16 length][u16 opcode][body]; length = 2 + body.</summary>
    public static byte[] Encode(ushort opcode, byte[] body)
    {
        var len = 2 + body.Length;
        if (len > 65534)
            throw new InvalidOperationException($"packet 0x{opcode:X4} overflow");
        var buf = new byte[2 + len];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), (ushort)len);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), opcode);
        if (body.Length > 0)
            Buffer.BlockCopy(body, 0, buf, 4, body.Length);
        return buf;
    }

    public static bool TryReadFrame(List<byte> buffer, out ushort opcode, out byte[] body)
    {
        opcode = 0;
        body = [];
        if (buffer.Count < 2)
            return false;
        var length = BinaryPrimitives.ReadUInt16LittleEndian(buffer.ToArray().AsSpan(0, 2));
        if (buffer.Count < 2 + length)
            return false;
        var frame = buffer.GetRange(0, 2 + length).ToArray();
        buffer.RemoveRange(0, 2 + length);
        if (length < 2)
            return false;
        opcode = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2, 2));
        body = new byte[length - 2];
        if (body.Length > 0)
            Buffer.BlockCopy(frame, 4, body, 0, body.Length);
        return true;
    }
}

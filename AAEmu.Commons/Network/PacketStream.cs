using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

using AAEmu.Commons.Conversion;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Utils;

using SBuffer = System.Buffer;

namespace AAEmu.Commons.Network;

/// <summary>
/// Class to manage, merge, read and write packets. 
/// Methods have equal names as BinaryReader and BinaryWriter.
/// → Class has dependency from stream endianess!
/// </summary>
public class PacketStream : ICloneable, IComparable
{
    #region Data

    private const int DefaultSize = 128;

    #endregion // Data

    #region Properties

    public byte[] Buffer { get; private set; }

    public int Count { get; private set; }

    public int Capacity => Buffer.Length;

    public int Pos { get; set; }

    public bool IsLittleEndian { get; set; }
    public bool HasBytes => Pos < Count;
    public int LeftBytes => Count - Pos;

    public EndianBitConverter Converter =>
        (IsLittleEndian ? EndianBitConverter.Little : EndianBitConverter.Big);

    #endregion // Properties

    #region Operators & Casts

    public byte this[int index]
    {
        set => Buffer[index] = value;
        get => Buffer[index];
    }

    public static explicit operator PacketStream(byte[] o)
    {
        return new PacketStream(o);
    }

    public static implicit operator byte[](PacketStream o)
    {
        return o.GetBytes();
    }

    #endregion // Operators & Casts

    #region Constructor

    public PacketStream() : this(DefaultSize)
    {
    }

    public PacketStream(int count)
    {
        IsLittleEndian = true;
        Reserve(count);
    }

    public PacketStream(PacketStream sourcePacketStream)
    {
        IsLittleEndian = sourcePacketStream.IsLittleEndian;
        Replace(sourcePacketStream);
    }

    public PacketStream(byte[] sourcebytes)
    {
        IsLittleEndian = true;
        Replace(sourcebytes);
    }

    public PacketStream(byte[] sourcebytes, int offset, int count)
    {
        IsLittleEndian = true;
        Replace(sourcebytes, offset, count);
    }

    public PacketStream(PacketStream sourcePacketStream, int offset, int count)
    {
        IsLittleEndian = sourcePacketStream.IsLittleEndian;
        Replace(sourcePacketStream, offset, count);
    }

    #endregion // Constructor

    #region Reserve & Roundup

    private static byte[] Roundup(int length)
    {
        var i = 16;
        while (length > i)
            i <<= 1;
        return new byte[i];
    }

    /// <summary>
    /// Initializes buffer for this stream with provided minimum size.
    /// </summary>
    /// <param name="count">Minimum buffer size.</param>
    public void Reserve(int count)
    {
        if (Buffer == null)
        {
            Buffer = Roundup(count);
        }
        else if (count > Buffer.Length)
        {
            var newBuffer = Roundup(count);
            SBuffer.BlockCopy(Buffer, 0, newBuffer, 0, Count);
            Buffer = newBuffer;
        }
    }

    #endregion // Reserve & Roundup

    #region Replace

    /// <summary>
    /// Replace current PacketStream with provided one.
    /// </summary>
    /// <param name="stream">Replace stream.</param>
    /// <returns></returns>
    public PacketStream Replace(PacketStream stream)
    {
        return Replace(stream.Buffer, 0, stream.Count);
    }

    /// <summary>
    /// Replace current PacketStream with provided byte array.
    /// </summary>
    /// <param name="bytes">Array of bytes</param>
    /// <returns></returns>
    public PacketStream Replace(byte[] bytes)
    {
        return Replace(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Replace current PacketStream with some bytes from provided stream.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public PacketStream Replace(PacketStream stream, int offset, int count)
    {
        // remove garbage left after copying from PacketStream stream
        return Replace(stream.Buffer, offset, count);
    }

    /// <summary>
    /// Replace current PacketStream with some bytes from provided byte array.
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public PacketStream Replace(byte[] bytes, int offset, int count)
    {
        Reserve(count);
        SBuffer.BlockCopy(bytes, offset, Buffer, 0, count);
        Count = count;
        return this;
    }

    #endregion // Replace

    #region Clear

    /// <summary>
    /// Clears current stream.
    /// </summary>
    /// <returns></returns>
    public PacketStream Clear()
    {
        Array.Clear(Buffer, 0, Count);
        Count = 0;
        return this;
    }

    #endregion // Clear

    #region PushBack

    public PacketStream PushBack(byte b)
    {
        Reserve(Count + 1);
        Buffer[(Count++)] = b;
        return this;
    }

    #endregion // PushBack

    #region Swap

    public PacketStream Swap(PacketStream swapStream)
    {
        var i = Count;
        Count = swapStream.Count;
        swapStream.Count = i;

        var temp = swapStream.Buffer;
        swapStream.Buffer = Buffer;
        Buffer = temp;
        return this;
    }

    #endregion // Swap

    #region Rollback

    public void Rollback()
    {
        Pos = 0;
    }

    public void Rollback(int len)
    {
        Pos -= len;
    }

    #endregion // Rollback

    #region Erase

    public PacketStream Erase(int from)
    {
        return Erase(from, Count);
    }

    public PacketStream Erase(int from, int to)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (Count < to)
            throw new ArgumentOutOfRangeException(nameof(to));

        // shift good content to erase
        SBuffer.BlockCopy(Buffer, to, Buffer, from, Count -= to - from);
        return this;
    }

    #endregion // Erase

    #region Insert

    public PacketStream Insert(int offset, PacketStream copyStream)
    {
        return Insert(offset, copyStream.Buffer, 0, copyStream.Count);
    }

    public PacketStream Insert(int offset, byte[] copyArray)
    {
        return Insert(offset, copyArray, 0, copyArray.Length);
    }

    public PacketStream Insert(int offset, PacketStream copyStream, int copyStreamOffset, int count)
    {
        return Insert(offset, copyStream.Buffer, copyStreamOffset, count);
    }

    public PacketStream Insert(int offset, byte[] copyArray, int copyArrayOffset, int count)
    {
        Reserve(Count + count);
        // move data from position offset to position offset + count
        SBuffer.BlockCopy(Buffer, offset, Buffer, offset + count, Count - offset);
        // copy the new data array to position offset
        SBuffer.BlockCopy(copyArray, copyArrayOffset, Buffer, offset, count);
        Count += count;
        return this;
    }

    #endregion // Insert

    #region GetBytes

    public byte[] GetBytes()
    {
        var temp = new byte[Count];
        SBuffer.BlockCopy(Buffer, 0, temp, 0, Count);
        return temp;
    }

    #endregion // GetBytes

    #region Read Primitive Types

    public bool ReadBoolean()
    {
        return ReadByte() == 1;
    }

    public byte ReadByte()
    {
        if (Pos + 1 > Count)
            throw new MarshalException();
        return this[Pos++];
    }

    public sbyte ReadSByte()
    {
        if (Pos + 1 > Count)
            throw new MarshalException();
        return (sbyte)this[Pos++];
    }

    public byte[] ReadBytes(int count)
    {
        if (Pos + count > Count)
            throw new MarshalException();

        var result = new byte[count];
        SBuffer.BlockCopy(Buffer, Pos, result, 0, count);
        Pos += count;
        return result;
    }

    public byte[] ReadBytes()
    {
        var count = ReadInt16();

        if (Pos + count > Count)
            throw new MarshalException();

        var result = new byte[count];
        SBuffer.BlockCopy(Buffer, Pos, result, 0, count);
        Pos += count;
        return result;
    }

    public char ReadChar()
    {
        if (Pos + 2 > Count)
            throw new MarshalException();

        var result = Converter.ToChar(Buffer, Pos);
        Pos += 2;

        return result;
    }

    public char[] ReadChars(int count)
    {
        if (Pos + 2 * count > Count)
            throw new MarshalException();

        var result = new char[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadChar();

        return result;
    }

    public short ReadInt16()
    {
        if (Pos + 2 > Count)
            throw new MarshalException();

        var result = Converter.ToInt16(Buffer, Pos);
        Pos += 2;

        return result;
    }

    public int ReadInt32()
    {
        if (Pos + 4 > Count)
            throw new MarshalException();

        var result = Converter.ToInt32(Buffer, Pos);
        Pos += 4;

        return result;
    }

    public long ReadInt64()
    {
        if (Pos + 8 > Count)
            throw new MarshalException();

        var result = Converter.ToInt64(Buffer, Pos);
        Pos += 8;

        return result;
    }

    public ushort ReadUInt16()
    {
        if (Pos + 2 > Count)
            throw new MarshalException();

        var result = Converter.ToUInt16(Buffer, Pos);
        Pos += 2;

        return result;
    }

    public uint ReadUInt32()
    {
        if (Pos + 4 > Count)
            throw new MarshalException();

        var result = Converter.ToUInt32(Buffer, Pos);
        Pos += 4;

        return result;
    }

    public uint ReadBc()
    {
        if (Pos + 3 > Count)
            throw new MarshalException();

        var result = ReadUInt16() + (ReadByte() << 16);

        return (uint)result;
    }

    public ulong ReadUInt64()
    {
        if (Pos + 8 > Count)
            throw new MarshalException();

        var result = Converter.ToUInt64(Buffer, Pos);
        Pos += 8;

        return result;
    }

    public float ReadSingle()
    {
        if (Pos + 4 > Count)
            throw new MarshalException();

        var result = Converter.ToSingle(Buffer, Pos);
        Pos += 4;

        return result;
    }

    public double ReadDouble()
    {
        if (Pos + 8 > Count)
            throw new MarshalException();

        var result = Converter.ToDouble(Buffer, Pos);
        Pos += 8;

        return result;
    }

    #endregion // Read Primitive Types

    #region Read Complex Types

    public PacketStream ReadPacketStream()
    {
        var i = ReadInt16();
        if (Pos + i > Count)
            throw new MarshalException();
        var newStream = new PacketStream(Buffer, Pos, i);
        Pos += i;
        return newStream;
    }

    public PacketStream Read(PacketStream stream)
    {
        var i = ReadInt16();
        if (Pos + i > Count)
            throw new MarshalException();
        stream.Replace(Buffer, Pos, i);
        Pos += i;
        return this;
    }

    public void Read(PacketMarshaler paramMarshal)
    {
        paramMarshal.Read(this);
    }

    public T Read<T>() where T : PacketMarshaler, new()
    {
        var t = new T();
        Read(t);
        return t;
    }

    public List<T> ReadCollection<T>() where T : PacketMarshaler, new()
    {
        var count = ReadInt32();
        var collection = new List<T>();
        for (var i = 0; i < count; i++)
        {
            var t = new T();
            Read(t);
            collection.Add(t);
        }

        return collection;
    }

    public DateTime ReadDateTime()
    {
        return Helpers.UnixTime(ReadInt64());
    }

    public long[] ReadPisc(int count)
    {
        var result = new long[count];
        var pish = new BitArray(new byte[] { ReadByte() });
        for (var index = 0; index < count * 2; index += 2)
        {
            if (pish[index] && pish[index + 1]) // uint
                result[index / 2] = ReadUInt32();
            else if (pish[index + 1]) // bc
                result[index / 2] = ReadBc();
            else if (pish[index]) // ushort
                result[index / 2] = ReadUInt16();
            else // byte
                result[index / 2] = ReadByte();
        }

        return result;
    }

    public long[] ReadPiscW(int hcount)
    {
        if (hcount <= 0)
        {
            return new long[0];
        }

        var values = new long[hcount];
        var index = 0;

        do
        {
            var pcount = 4;
            if (hcount <= 4)
                pcount = hcount;

            switch (pcount)
            {
                case 1:
                    {
                        var temp = ReadPisc(1);
                        values[index] = temp[0];
                        index += 1;
                        break;
                    }
                case 2:
                    {
                        var temp = ReadPisc(2);
                        values[index] = temp[0];
                        values[index + 1] = temp[1];
                        index += 2;
                        break;
                    }
                case 3:
                    {
                        var temp = ReadPisc(3);
                        values[index] = temp[0];
                        values[index + 1] = temp[1];
                        values[index + 2] = temp[2];
                        index += 3;
                        break;
                    }
                case 4:
                    {
                        var temp = ReadPisc(4);
                        values[index] = temp[0];
                        values[index + 1] = temp[1];
                        values[index + 2] = temp[2];
                        values[index + 3] = temp[3];
                        index += 4;
                        break;
                    }
            }

            hcount -= pcount;
        } while (hcount > 0);

        return values;
    }
    public (float x, float y, float z) ReadPosition()
    {
        var position = ReadBytes(9);
        return Helpers.ConvertPosition(position);
    }

    public Quaternion ReadQuaternionShort()
    {
        var quatX = Convert.ToSingle(ReadInt16() * 0.000030518509f);
        var quatY = Convert.ToSingle(ReadInt16() * 0.000030518509f);
        var quatZ = Convert.ToSingle(ReadInt16() * 0.000030518509f);
        var quatNorm = quatX * quatX + quatY * quatY + quatZ * quatZ;

        var quatW = 0.0f;
        if (quatNorm < 0.99750)
        {
            quatW = (float)Math.Sqrt(1.0 - quatNorm);
        }

        var quat = new Quaternion(quatX, quatY, quatZ, quatW);

        return quat;
    }

    public Vector3 ReadVector3Single()
    {
        var x = ReadSingle();
        var y = ReadSingle();
        var z = ReadSingle();
        var temp = new Vector3(x, y, z);
        return temp;
    }

    public Vector3 ReadVector3Short()
    {
        var x = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
        var y = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
        var z = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
        var temp = new Vector3(x, y, z);

        return temp;
    }

    #endregion // Read Complex Types

    #region Read Strings

    public string ReadString()
    {
        var i = ReadInt16();
        var strBuf = ReadBytes(i);
        return Encoding.UTF8.GetString(strBuf).Trim('\u0000');
    }

    public string ReadString(int len)
    {
        var strBuf = ReadBytes(len);
        return Encoding.UTF8.GetString(strBuf).Trim('\u0000');
    }

    #endregion // Read Strings

    #region Write Primitive Types

    public PacketStream Write(bool value)
    {
        return Write(value ? (byte)0x01 : (byte)0x00);
    }

    public PacketStream Write(byte value)
    {
        PushBack(value);
        return this;
    }

    public PacketStream Write(byte[] value, bool appendSize = false)
    {
        if (appendSize)
            Write((ushort)value.Length);
        return Insert(Count, value);
    }

    public PacketStream Write(sbyte value)
    {
        return Write((byte)value);
    }

    public PacketStream Write(char value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(char[] value)
    {
        foreach (var ch in value)
            Write(ch);
        return this;
    }

    public PacketStream Write(short value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(int value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(long value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(ushort value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(uint value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(ulong value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(float value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream Write(double value)
    {
        return Write(Converter.GetBytes(value));
    }

    public PacketStream WriteBc(uint value)
    {
        return Write(Converter.GetBytes(value, 3));
    }

    #endregion // Write Primitive Types

    #region Write Complex Types

    public PacketStream Write(PacketMarshaler value)
    {
        return value.Write(this);
    }

    public PacketStream Write<T>(ICollection<T> values) where T : PacketMarshaler
    {
        Write(values.Count);
        foreach (var marshaler in values)
            Write(marshaler);
        return this;
    }

    public PacketStream Write(PacketStream value, bool appendSize = true)
    {
        return Write(value.GetBytes(), appendSize);
    }

    public PacketStream Write(DateTime value)
    {
        return Write(Helpers.UnixTime(value));
    }

    public PacketStream Write(Guid value, bool appendSize = true)
    {
        return Write(value.ToByteArray(), appendSize);
    }

    public PacketStream WritePisc(params long[] values)
    {
        var pish = new BitArray(8);
        var temp = new PacketStream();
        var index = 0;
        foreach (var value in values)
        {
            if (value <= byte.MaxValue)
                temp.Write((byte)value);
            else if (value <= ushort.MaxValue)
            {
                pish[index] = true;
                temp.Write((ushort)value);
            }
            else if (value <= 0xffffff)
            {
                pish[index + 1] = true;
                temp.WriteBc((uint)value);
            }
            else
            {
                pish[index] = true;
                pish[index + 1] = true;
                temp.Write((uint)value);
            }

            index += 2;
        }

        var res = new byte[1];
        pish.CopyTo(res, 0);
        Write(res[0]);
        Write(temp, false);
        return this;
    }

    public PacketStream WritePiscW(int hcount, params long[] values)
    {
        if (hcount > 0)
        {
            var index = 0;
            do
            {
                var pcount = 4;
                if (hcount <= 4)
                    pcount = hcount;
                switch (pcount)
                {
                    case 1:
                        {
                            WritePisc(values[index]);
                            index += 1;
                            break;
                        }
                    case 2:
                        {
                            WritePisc(values[index], values[index + 1]);
                            index += 2;
                            break;
                        }
                    case 3:
                        {
                            WritePisc(values[index], values[index + 1], values[index + 2]);
                            index += 3;
                            break;
                        }
                    case 4:
                        {
                            WritePisc(values[index], values[index + 1], values[index + 2], values[index + 3]);
                            index += 4;
                            break;
                        }
                }
                hcount -= pcount;
            } while (hcount > 0);
        }
        return this;
    }

    public PacketStream WritePosition(float x, float y, float z)
    {
        var res = Helpers.ConvertPosition(x, y, z);
        Write(res);
        return this;
    }

    public PacketStream WritePosition(Vector3 pos)
    {
        var res = Helpers.ConvertPosition(pos.X, pos.Y, pos.Z);
        Write(res);
        return this;
    }

    public PacketStream WriteQuaternionShort(Quaternion values, bool scalar = false)
    {
        var temp = new PacketStream();
        try
        {
            temp.Write(Convert.ToInt16(values.X * 32767f));
            temp.Write(Convert.ToInt16(values.Y * 32767f));
            temp.Write(Convert.ToInt16(values.Z * 32767f));
        }
        catch
        {
            var res = new byte[6];
            temp.Write(res);
        }

        if (scalar)
        {
            temp.Write(Convert.ToInt16(values.W));
        }
        return Write(temp, false);
    }

    public PacketStream WriteVector3Single(Vector3 values)
    {
        var temp = new PacketStream();
        temp.Write(values.X);
        temp.Write(values.Y);
        temp.Write(values.Z);
        return Write(temp, false);
    }
    public PacketStream WriteVector3Short(Vector3 values)
    {
        var temp = new PacketStream();
        temp.Write(Convert.ToInt16(values.X * 32767f));
        temp.Write(Convert.ToInt16(values.Y * 32767f));
        temp.Write(Convert.ToInt16(values.Z * 32767f));
        return Write(temp, false);
    }

    #endregion // Write Complex Types

    #region Write Strings

    public PacketStream Write(string value, bool appendSize = true, bool appendTerminator = false)
    {
        var str = Encoding.UTF8.GetBytes(appendTerminator ? value + '\u0000' : value); // utf-8
        return Write(str, appendSize);
    }

    #endregion // Write Strings

    #region ToString

    public override string ToString()
    {
        return BitConverter.ToString(GetBytes());
    }

    #endregion // ToString

    #region Equals

    public bool Equals(PacketStream stream)
    {
        if (Count != stream.Count)
            return false;

        for (var i = 0; i < Count; i++)
            if (this[i] != stream[i])
                return false;

        return true;
    }

    public override bool Equals(object obj)
    {
        if (obj is PacketStream stream)
            return Equals(stream);
        return false;
    }

    public override int GetHashCode()
    {
        return Buffer.GetHashCode();
    }

    #endregion // Equals

    #region ICloneable Members

    public object Clone()
    {
        return new PacketStream(this);
    }

    #endregion

    #region IComparable Members

    public int CompareTo(object obj)
    {
        if (!(obj is PacketStream stream))
            throw new ArgumentException("Object is not a PacketStream instance");
        var count = Math.Min(Count, stream.Count);
        for (var i = 0; i < count; i++)
        {
            var k = this[i] - stream[i];
            if (k != 0)
                return k;
        }

        return Count - stream.Count;
    }

    #endregion
}

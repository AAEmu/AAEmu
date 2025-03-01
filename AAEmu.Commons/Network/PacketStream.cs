using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using AAEmu.Commons.Conversion;
using AAEmu.Commons.Utils;

using NLog;

using SBuffer = System.Buffer;

namespace AAEmu.Commons.Network;

/// <summary>
/// Class to manage, merge, read and write packets.
/// Methods have equal names as BinaryReader and BinaryWriter.
/// → Class has dependency from stream endianess!
/// </summary>
public class PacketStream : ICloneable, IComparable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    #region Data

    private const int DefaultSize = 128;

    #endregion // Data

    #region Properties

    /// <summary>
    /// Gets the buffer containing the packet data.
    /// </summary>
    public byte[] Buffer { get; private set; }

    /// <summary>
    /// Gets the number of bytes in the packet.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the capacity of the buffer.
    /// </summary>
    public int Capacity => Buffer.Length;

    /// <summary>
    /// Gets or sets the current position in the packet.
    /// </summary>
    public int Pos { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the packet uses little-endian byte order.
    /// </summary>
    public bool IsLittleEndian { get; set; }

    /// <summary>
    /// Gets a value indicating whether there are bytes left to read.
    /// </summary>
    public bool HasBytes => Pos < Count;

    /// <summary>
    /// Gets the number of bytes left to read.
    /// </summary>
    public int LeftBytes => Count - Pos;

    /// <summary>
    /// Gets the endian bit converter based on the current endianness.
    /// </summary>
    public EndianBitConverter Converter => (IsLittleEndian ? EndianBitConverter.Little : EndianBitConverter.Big);

    #endregion // Properties

    #region Operators & Casts

    /// <summary>
    /// Gets or sets the byte at the specified index.
    /// </summary>
    /// <param name="index">The index of the byte to get or set.</param>
    /// <returns>The byte at the specified index.</returns>
    public byte this[int index]
    {
        set => Buffer[index] = value;
        get => Buffer[index];
    }

    /// <summary>
    /// Explicitly converts a byte array to a PacketStream.
    /// </summary>
    /// <param name="o">The byte array to convert.</param>
    /// <returns>A new PacketStream containing the bytes.</returns>
    public static explicit operator PacketStream(byte[] o)
    {
        return new PacketStream(o);
    }

    /// <summary>
    /// Implicitly converts a PacketStream to a byte array.
    /// </summary>
    /// <param name="o">The PacketStream to convert.</param>
    /// <returns>The byte array containing the packet data.</returns>
    public static implicit operator byte[](PacketStream o)
    {
        return o.GetBytes();
    }

    #endregion // Operators & Casts

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the PacketStream class with the default size.
    /// </summary>
    public PacketStream() : this(DefaultSize)
    {
    }

    /// <summary>
    /// Initializes a new instance of the PacketStream class with the specified size.
    /// </summary>
    /// <param name="count">The initial size of the buffer.</param>
    public PacketStream(int count)
    {
        IsLittleEndian = true;
        Reserve(count);
    }

    /// <summary>
    /// Initializes a new instance of the PacketStream class by copying from another PacketStream.
    /// </summary>
    /// <param name="sourcePacketStream">The PacketStream to copy from.</param>
    public PacketStream(PacketStream sourcePacketStream)
    {
        IsLittleEndian = sourcePacketStream.IsLittleEndian;
        Replace(sourcePacketStream);
    }

    /// <summary>
    /// Initializes a new instance of the PacketStream class by copying from a byte array.
    /// </summary>
    /// <param name="sourcebytes">The byte array to copy from.</param>
    public PacketStream(byte[] sourcebytes)
    {
        IsLittleEndian = true;
        Replace(sourcebytes);
    }

    /// <summary>
    /// Initializes a new instance of the PacketStream class by copying from a byte array with an offset and count.
    /// </summary>
    /// <param name="sourcebytes">The byte array to copy from.</param>
    /// <param name="offset">The offset in the byte array to start copying from.</param>
    /// <param name="count">The number of bytes to copy.</param>
    public PacketStream(byte[] sourcebytes, int offset, int count)
    {
        IsLittleEndian = true;
        Replace(sourcebytes, offset, count);
    }

    /// <summary>
    /// Initializes a new instance of the PacketStream class by copying from another PacketStream with an offset and count.
    /// </summary>
    /// <param name="sourcePacketStream">The PacketStream to copy from.</param>
    /// <param name="offset">The offset in the PacketStream to start copying from.</param>
    /// <param name="count">The number of bytes to copy.</param>
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
    /// <returns>The current PacketStream.</returns>
    public PacketStream Replace(PacketStream stream)
    {
        return Replace(stream.Buffer, 0, stream.Count);
    }

    /// <summary>
    /// Replace current PacketStream with provided byte array.
    /// </summary>
    /// <param name="bytes">Array of bytes</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Replace(byte[] bytes)
    {
        return Replace(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Replace current PacketStream with some bytes from provided stream.
    /// </summary>
    /// <param name="stream">The PacketStream to copy from.</param>
    /// <param name="offset">The offset in the PacketStream to start copying from.</param>
    /// <param name="count">The number of bytes to copy.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Replace(PacketStream stream, int offset, int count)
    {
        // remove garbage left after copying from PacketStream stream
        return Replace(stream.Buffer, offset, count);
    }

    /// <summary>
    /// Replace current PacketStream with some bytes from provided byte array.
    /// </summary>
    /// <param name="bytes">The byte array to copy from.</param>
    /// <param name="offset">The offset in the byte array to start copying from.</param>
    /// <param name="count">The number of bytes to copy.</param>
    /// <returns>The current PacketStream.</returns>
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
    /// <returns>The current PacketStream.</returns>
    public PacketStream Clear()
    {
        Array.Clear(Buffer, 0, Count);
        Count = 0;
        return this;
    }

    #endregion // Clear

    #region PushBack

    /// <summary>
    /// Pushes a byte to the end of the stream.
    /// </summary>
    /// <param name="b">The byte to push.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream PushBack(byte b)
    {
        Reserve(Count + 1);
        Buffer[(Count++)] = b;
        return this;
    }

    #endregion // PushBack

    #region Swap

    /// <summary>
    /// Swaps the contents of this PacketStream with another PacketStream.
    /// </summary>
    /// <param name="swapStream">The PacketStream to swap with.</param>
    /// <returns>The current PacketStream.</returns>
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

    /// <summary>
    /// Rolls back the position to the start of the stream.
    /// </summary>
    public void Rollback()
    {
        Pos = 0;
    }

    /// <summary>
    /// Rolls back the position by the specified number of bytes.
    /// </summary>
    /// <param name="len">The number of bytes to roll back.</param>
    public void Rollback(int len)
    {
        Pos -= len;
    }

    #endregion // Rollback

    #region Erase

    /// <summary>
    /// Erases bytes from the specified position to the end of the stream.
    /// </summary>
    /// <param name="from">The position to start erasing from.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Erase(int from)
    {
        return Erase(from, Count);
    }

    /// <summary>
    /// Erases bytes from the specified start position to the specified end position.
    /// </summary>
    /// <param name="from">The position to start erasing from.</param>
    /// <param name="to">The position to end erasing at.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Erase(int from, int to)
    {
        if (from > to)
        {
            Logger.Error("Invalid range for Erase: from > to");
            return this;
        }
        if (Count < to)
        {
            Logger.Error("Invalid range for Erase: to > Count");
            return this;
        }

        // shift good content to erase
        SBuffer.BlockCopy(Buffer, to, Buffer, from, Count -= to - from);
        return this;
    }

    #endregion // Erase

    #region Insert

    /// <summary>
    /// Inserts a PacketStream into the current stream at the specified offset.
    /// </summary>
    /// <param name="offset">The offset to insert at.</param>
    /// <param name="copyStream">The PacketStream to insert.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Insert(int offset, PacketStream copyStream)
    {
        return Insert(offset, copyStream.Buffer, 0, copyStream.Count);
    }

    /// <summary>
    /// Inserts a byte array into the current stream at the specified offset.
    /// </summary>
    /// <param name="offset">The offset to insert at.</param>
    /// <param name="copyArray">The byte array to insert.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Insert(int offset, byte[] copyArray)
    {
        return Insert(offset, copyArray, 0, copyArray.Length);
    }

    /// <summary>
    /// Inserts a portion of a PacketStream into the current stream at the specified offset.
    /// </summary>
    /// <param name="offset">The offset to insert at.</param>
    /// <param name="copyStream">The PacketStream to insert from.</param>
    /// <param name="copyStreamOffset">The offset in the PacketStream to start copying from.</param>
    /// <param name="count">The number of bytes to insert.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Insert(int offset, PacketStream copyStream, int copyStreamOffset, int count)
    {
        return Insert(offset, copyStream.Buffer, copyStreamOffset, count);
    }

    /// <summary>
    /// Inserts a portion of a byte array into the current stream at the specified offset.
    /// </summary>
    /// <param name="offset">The offset to insert at.</param>
    /// <param name="copyArray">The byte array to insert from.</param>
    /// <param name="copyArrayOffset">The offset in the byte array to start copying from.</param>
    /// <param name="count">The number of bytes to insert.</param>
    /// <returns>The current PacketStream.</returns>
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

    /// <summary>
    /// Gets a copy of the bytes in the stream.
    /// </summary>
    /// <returns>A byte array containing the packet data.</returns>
    public byte[] GetBytes()
    {
        var temp = new byte[Count];
        SBuffer.BlockCopy(Buffer, 0, temp, 0, Count);
        return temp;
    }

    #endregion // GetBytes

    #region Read Primitive Types

    /// <summary>
    /// Reads a boolean value from the stream.
    /// </summary>
    /// <returns>The boolean value read from the stream.</returns>
    public bool ReadBoolean()
    {
        return ReadByte() == 1;
    }

    /// <summary>
    /// Reads a byte from the stream.
    /// </summary>
    /// <returns>The byte read from the stream.</returns>
    public byte ReadByte()
    {
        if (Pos + 1 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }
        return this[Pos++];
    }

    /// <summary>
    /// Reads a signed byte from the stream.
    /// </summary>
    /// <returns>The signed byte read from the stream.</returns>
    public sbyte ReadSByte()
    {
        if (Pos + 1 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }
        return (sbyte)this[Pos++];
    }

    /// <summary>
    /// Reads a specified number of bytes from the stream.
    /// </summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>A byte array containing the bytes read from the stream.</returns>
    public byte[] ReadBytes(int count)
    {
        if (Pos + count > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return new byte[0]; // Возвращаем пустой массив
        }

        var result = new byte[count];
        SBuffer.BlockCopy(Buffer, Pos, result, 0, count);
        Pos += count;
        return result;
    }

    /// <summary>
    /// Reads a byte array from the stream, where the length is specified by a preceding short.
    /// </summary>
    /// <returns>A byte array containing the bytes read from the stream.</returns>
    public byte[] ReadBytes()
    {
        var count = ReadInt16();

        if (Pos + count > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return new byte[0]; // Возвращаем пустой массив
        }

        var result = new byte[count];
        SBuffer.BlockCopy(Buffer, Pos, result, 0, count);
        Pos += count;
        return result;
    }

    /// <summary>
    /// Reads a character from the stream.
    /// </summary>
    /// <returns>The character read from the stream.</returns>
    public char ReadChar()
    {
        if (Pos + 2 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return '\0'; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToChar(Buffer, Pos);
        Pos += 2;

        return result;
    }

    /// <summary>
    /// Reads a specified number of characters from the stream.
    /// </summary>
    /// <param name="count">The number of characters to read.</param>
    /// <returns>A character array containing the characters read from the stream.</returns>
    public char[] ReadChars(int count)
    {
        if (Pos + 2 * count > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return new char[0]; // Возвращаем пустой массив
        }

        var result = new char[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadChar();

        return result;
    }

    /// <summary>
    /// Reads a 16-bit signed integer from the stream.
    /// </summary>
    /// <returns>The 16-bit signed integer read from the stream.</returns>
    public short ReadInt16()
    {
        if (Pos + 2 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToInt16(Buffer, Pos);
        Pos += 2;

        return result;
    }

    /// <summary>
    /// Reads a 32-bit signed integer from the stream.
    /// </summary>
    /// <returns>The 32-bit signed integer read from the stream.</returns>
    public int ReadInt32()
    {
        if (Pos + 4 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToInt32(Buffer, Pos);
        Pos += 4;

        return result;
    }

    /// <summary>
    /// Reads a 64-bit signed integer from the stream.
    /// </summary>
    /// <returns>The 64-bit signed integer read from the stream.</returns>
    public long ReadInt64()
    {
        if (Pos + 8 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToInt64(Buffer, Pos);
        Pos += 8;

        return result;
    }

    /// <summary>
    /// Reads a 16-bit unsigned integer from the stream.
    /// </summary>
    /// <returns>The 16-bit unsigned integer read from the stream.</returns>
    public ushort ReadUInt16()
    {
        if (Pos + 2 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToUInt16(Buffer, Pos);
        Pos += 2;

        return result;
    }

    /// <summary>
    /// Reads a 32-bit unsigned integer from the stream.
    /// </summary>
    /// <returns>The 32-bit unsigned integer read from the stream.</returns>
    public uint ReadUInt32()
    {
        if (Pos + 4 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToUInt32(Buffer, Pos);
        Pos += 4;

        return result;
    }

    /// <summary>
    /// Reads a 24-bit unsigned integer from the stream.
    /// </summary>
    /// <returns>The 24-bit unsigned integer read from the stream.</returns>
    public uint ReadBc()
    {
        if (Pos + 3 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = ReadUInt16() + (ReadByte() << 16);

        return (uint)result;
    }

    /// <summary>
    /// Reads a 64-bit unsigned integer from the stream.
    /// </summary>
    /// <returns>The 64-bit unsigned integer read from the stream.</returns>
    public ulong ReadUInt64()
    {
        if (Pos + 8 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToUInt64(Buffer, Pos);
        Pos += 8;

        return result;
    }

    /// <summary>
    /// Reads a single-precision floating-point number from the stream.
    /// </summary>
    /// <returns>The single-precision floating-point number read from the stream.</returns>
    public float ReadSingle()
    {
        if (Pos + 4 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToSingle(Buffer, Pos);
        Pos += 4;

        return result;
    }

    /// <summary>
    /// Reads a double-precision floating-point number from the stream.
    /// </summary>
    /// <returns>The double-precision floating-point number read from the stream.</returns>
    public double ReadDouble()
    {
        if (Pos + 8 > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return 0; // Возвращаем значение по умолчанию
        }

        var result = Converter.ToDouble(Buffer, Pos);
        Pos += 8;

        return result;
    }

    #endregion // Read Primitive Types

    #region Read Complex Types

    /// <summary>
    /// Reads a PacketStream from the current stream.
    /// </summary>
    /// <returns>A new PacketStream containing the read data.</returns>
    public PacketStream ReadPacketStream()
    {
        var i = ReadInt16();
        if (Pos + i > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return new PacketStream(); // Возвращаем пустой PacketStream
        }
        var newStream = new PacketStream(Buffer, Pos, i);
        Pos += i;
        return newStream;
    }

    /// <summary>
    /// Reads a PacketStream into the provided stream.
    /// </summary>
    /// <param name="stream">The PacketStream to read into.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Read(PacketStream stream)
    {
        var i = ReadInt16();
        if (Pos + i > Count)
        {
            Logger.Error("Attempted to read beyond the end of the stream.");
            return this; // Возвращаем текущий PacketStream
        }
        stream.Replace(Buffer, Pos, i);
        Pos += i;
        return this;
    }

    /// <summary>
    /// Reads a PacketMarshaler from the stream.
    /// </summary>
    /// <param name="paramMarshal">The PacketMarshaler to read into.</param>
    public void Read(PacketMarshaler paramMarshal)
    {
        try
        {
            paramMarshal.Read(this);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading PacketMarshaler.");
        }
    }

    /// <summary>
    /// Reads a PacketMarshaler of type T from the stream.
    /// </summary>
    /// <typeparam name="T">The type of PacketMarshaler to read.</typeparam>
    /// <returns>A new instance of the PacketMarshaler.</returns>
    public T Read<T>() where T : PacketMarshaler, new()
    {
        var t = new T();
        try
        {
            Read(t);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading PacketMarshaler.");
        }
        return t;
    }

    /// <summary>
    /// Reads a collection of PacketMarshaler objects from the stream.
    /// </summary>
    /// <typeparam name="T">The type of PacketMarshaler to read.</typeparam>
    /// <returns>A list of PacketMarshaler objects.</returns>
    public List<T> ReadCollection<T>() where T : PacketMarshaler, new()
    {
        var count = ReadInt32();
        var collection = new List<T>();
        for (var i = 0; i < count; i++)
        {
            var t = new T();
            try
            {
                Read(t);
                collection.Add(t);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reading PacketMarshaler collection.");
            }
        }

        return collection;
    }

    /// <summary>
    /// Reads a DateTime from the stream.
    /// </summary>
    /// <returns>The DateTime read from the stream.</returns>
    public DateTime ReadDateTime()
    {
        try
        {
            return Helpers.UnixTime(ReadInt64());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading DateTime.");
            return DateTime.MinValue; // Возвращаем значение по умолчанию
        }
    }

    /// <summary>
    /// Reads a PISC (Packet Integer Size Compression) array from the stream.
    /// </summary>
    /// <param name="count">The number of values to read.</param>
    /// <returns>An array of long values read from the stream.</returns>
    public long[] ReadPisc(int count)
    {
        var result = new long[count];
        try
        {
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
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading PISC array.");
        }
        return result;
    }

    /// <summary>
    /// Reads a PISC (Packet Integer Size Compression) array from the stream with a specified count.
    /// </summary>
    /// <param name="hcount">The number of values to read.</param>
    /// <returns>An array of long values read from the stream.</returns>
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

            try
            {
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
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reading PISC array.");
            }

            hcount -= pcount;
        } while (hcount > 0);

        return values;
    }

    /// <summary>
    /// Reads a position (x, y, z) from the stream.
    /// </summary>
    /// <returns>A tuple containing the x, y, and z coordinates.</returns>
    public (float x, float y, float z) ReadPosition()
    {
        try
        {
            var position = ReadBytes(9);
            return Helpers.ConvertPosition(position);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading position.");
            return (0, 0, 0); // Возвращаем значение по умолчанию
        }
    }

    /// <summary>
    /// Reads a quaternion from the stream.
    /// </summary>
    /// <returns>The quaternion read from the stream.</returns>
    public Quaternion ReadQuaternionShort()
    {
        try
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
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading quaternion.");
            return Quaternion.Identity; // Возвращаем значение по умолчанию
        }
    }

    /// <summary>
    /// Reads a Vector3 from the stream.
    /// </summary>
    /// <returns>The Vector3 read from the stream.</returns>
    public Vector3 ReadVector3Single()
    {
        try
        {
            var x = ReadSingle();
            var y = ReadSingle();
            var z = ReadSingle();
            var temp = new Vector3(x, y, z);
            return temp;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading Vector3.");
            return Vector3.Zero; // Возвращаем значение по умолчанию
        }
    }

    /// <summary>
    /// Reads a Vector3 from the stream using short values.
    /// </summary>
    /// <returns>The Vector3 read from the stream.</returns>
    public Vector3 ReadVector3Short()
    {
        try
        {
            var x = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
            var y = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
            var z = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
            var temp = new Vector3(x, y, z);

            return temp;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading Vector3.");
            return Vector3.Zero; // Возвращаем значение по умолчанию
        }
    }

    #endregion // Read Complex Types

    #region Read Strings

    /// <summary>
    /// Reads a string from the stream.
    /// </summary>
    /// <returns>The string read from the stream.</returns>
    public string ReadString()
    {
        try
        {
            var i = ReadInt16();
            var strBuf = ReadBytes(i);
            return Encoding.UTF8.GetString(strBuf).Trim('\u0000');
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading string.");
            return string.Empty; // Возвращаем значение по умолчанию
        }
    }

    /// <summary>
    /// Reads a string of a specified length from the stream.
    /// </summary>
    /// <param name="len">The length of the string to read.</param>
    /// <returns>The string read from the stream.</returns>
    public string ReadString(int len)
    {
        try
        {
            var strBuf = ReadBytes(len);
            return Encoding.UTF8.GetString(strBuf).Trim('\u0000');
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading string.");
            return string.Empty; // Возвращаем значение по умолчанию
        }
    }

    #endregion // Read Strings

    #region Write Primitive Types

    /// <summary>
    /// Writes a boolean value to the stream.
    /// </summary>
    /// <param name="value">The boolean value to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(bool value)
    {
        return Write(value ? (byte)0x01 : (byte)0x00);
    }

    /// <summary>
    /// Writes a byte to the stream.
    /// </summary>
    /// <param name="value">The byte to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(byte value)
    {
        PushBack(value);
        return this;
    }

    /// <summary>
    /// Writes a byte array to the stream.
    /// </summary>
    /// <param name="value">The byte array to write.</param>
    /// <param name="appendSize">Whether to append the size of the array before writing it.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(byte[] value, bool appendSize = false)
    {
        if (appendSize)
            Write((ushort)value.Length);
        return Insert(Count, value);
    }

    /// <summary>
    /// Writes a signed byte to the stream.
    /// </summary>
    /// <param name="value">The signed byte to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(sbyte value)
    {
        return Write((byte)value);
    }

    /// <summary>
    /// Writes a character to the stream.
    /// </summary>
    /// <param name="value">The character to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(char value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a character array to the stream.
    /// </summary>
    /// <param name="value">The character array to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(char[] value)
    {
        foreach (var ch in value)
            Write(ch);
        return this;
    }

    /// <summary>
    /// Writes a 16-bit signed integer to the stream.
    /// </summary>
    /// <param name="value">The 16-bit signed integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(short value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a 32-bit signed integer to the stream.
    /// </summary>
    /// <param name="value">The 32-bit signed integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(int value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a 64-bit signed integer to the stream.
    /// </summary>
    /// <param name="value">The 64-bit signed integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(long value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a 16-bit unsigned integer to the stream.
    /// </summary>
    /// <param name="value">The 16-bit unsigned integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(ushort value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer to the stream.
    /// </summary>
    /// <param name="value">The 32-bit unsigned integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(uint value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a 64-bit unsigned integer to the stream.
    /// </summary>
    /// <param name="value">The 64-bit unsigned integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(ulong value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a single-precision floating-point number to the stream.
    /// </summary>
    /// <param name="value">The single-precision floating-point number to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(float value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a double-precision floating-point number to the stream.
    /// </summary>
    /// <param name="value">The double-precision floating-point number to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(double value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// Writes a 24-bit unsigned integer to the stream.
    /// </summary>
    /// <param name="value">The 24-bit unsigned integer to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WriteBc(uint value)
    {
        return Write(Converter.GetBytes(value, 3));
    }

    #endregion // Write Primitive Types

    #region Write Complex Types

    /// <summary>
    /// Writes a PacketMarshaler to the stream.
    /// </summary>
    /// <param name="value">The PacketMarshaler to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(PacketMarshaler value)
    {
        try
        {
            return value.Write(this);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing PacketMarshaler.");
            return this;
        }
    }

    /// <summary>
    /// Writes a collection of PacketMarshaler objects to the stream.
    /// </summary>
    /// <typeparam name="T">The type of PacketMarshaler to write.</typeparam>
    /// <param name="values">The collection of PacketMarshaler objects to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write<T>(ICollection<T> values) where T : PacketMarshaler
    {
        try
        {
            Write(values.Count);
            foreach (var marshaler in values)
                Write(marshaler);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing PacketMarshaler collection.");
        }
        return this;
    }

    /// <summary>
    /// Writes a PacketStream to the stream.
    /// </summary>
    /// <param name="value">The PacketStream to write.</param>
    /// <param name="appendSize">Whether to append the size of the PacketStream before writing it.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(PacketStream value, bool appendSize = true)
    {
        try
        {
            return Write(value.GetBytes(), appendSize);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing PacketStream.");
            return this;
        }
    }

    /// <summary>
    /// Writes a DateTime to the stream.
    /// </summary>
    /// <param name="value">The DateTime to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(DateTime value)
    {
        try
        {
            return Write(Helpers.UnixTime(value));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing DateTime.");
            return this;
        }
    }

    /// <summary>
    /// Writes a Guid to the stream.
    /// </summary>
    /// <param name="value">The Guid to write.</param>
    /// <param name="appendSize">Whether to append the size of the Guid before writing it.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(Guid value, bool appendSize = true)
    {
        try
        {
            return Write(value.ToByteArray(), appendSize);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing Guid.");
            return this;
        }
    }

    /// <summary>
    /// Writes a PISC (Packet Integer Size Compression) array to the stream.
    /// </summary>
    /// <param name="values">The values to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WritePisc(params long[] values)
    {
        try
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
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing PISC array.");
        }
        return this;
    }

    /// <summary>
    /// Writes a PISC (Packet Integer Size Compression) array to the stream with a specified count.
    /// </summary>
    /// <param name="hcount">The number of values to write.</param>
    /// <param name="values">The values to write.</param>
    /// <returns>The current PacketStream.</returns>
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
                try
                {
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
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error writing PISC array.");
                }

                hcount -= pcount;
            } while (hcount > 0);
        }
        return this;
    }

    /// <summary>
    /// Writes a position (x, y, z) to the stream.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="z">The z coordinate.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WritePosition(float x, float y, float z)
    {
        try
        {
            var res = Helpers.ConvertPosition(x, y, z);
            Write(res);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing position.");
        }
        return this;
    }

    /// <summary>
    /// Writes a position (Vector3) to the stream.
    /// </summary>
    /// <param name="pos">The position to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WritePosition(Vector3 pos)
    {
        try
        {
            var res = Helpers.ConvertPosition(pos.X, pos.Y, pos.Z);
            Write(res);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing position.");
        }
        return this;
    }

    /// <summary>
    /// Writes a quaternion to the stream.
    /// </summary>
    /// <param name="values">The quaternion to write.</param>
    /// <param name="scalar">Whether to include the scalar component.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WriteQuaternionShort(Quaternion values, bool scalar = false)
    {
        try
        {
            var temp = new PacketStream();
            temp.Write(Convert.ToInt16(values.X * 32767f));
            temp.Write(Convert.ToInt16(values.Y * 32767f));
            temp.Write(Convert.ToInt16(values.Z * 32767f));

            if (scalar)
            {
                temp.Write(Convert.ToInt16(values.W));
            }
            return Write(temp, false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing quaternion.");
            return this;
        }
    }

    /// <summary>
    /// Writes a Vector3 to the stream.
    /// </summary>
    /// <param name="values">The Vector3 to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WriteVector3Single(Vector3 values)
    {
        try
        {
            var temp = new PacketStream();
            temp.Write(values.X);
            temp.Write(values.Y);
            temp.Write(values.Z);
            return Write(temp, false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing Vector3.");
            return this;
        }
    }

    /// <summary>
    /// Writes a Vector3 to the stream using short values.
    /// </summary>
    /// <param name="values">The Vector3 to write.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream WriteVector3Short(Vector3 values)
    {
        try
        {
            var temp = new PacketStream();
            temp.Write(Convert.ToInt16(values.X * 32767f));
            temp.Write(Convert.ToInt16(values.Y * 32767f));
            temp.Write(Convert.ToInt16(values.Z * 32767f));
            return Write(temp, false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing Vector3.");
            return this;
        }
    }

    #endregion // Write Complex Types

    #region Write Strings

    /// <summary>
    /// Writes a string to the stream.
    /// </summary>
    /// <param name="value">The string to write.</param>
    /// <param name="appendSize">Whether to append the size of the string before writing it.</param>
    /// <param name="appendTerminator">Whether to append a null terminator to the string.</param>
    /// <returns>The current PacketStream.</returns>
    public PacketStream Write(string value, bool appendSize = true, bool appendTerminator = false)
    {
        try
        {
            var str = Encoding.UTF8.GetBytes(appendTerminator ? value + '\u0000' : value); // utf-8
            return Write(str, appendSize);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error writing string.");
            return this;
        }
    }

    #endregion // Write Strings

    #region ToString

    /// <summary>
    /// Returns a string representation of the packet data.
    /// </summary>
    /// <returns>A string representation of the packet data.</returns>
    public override string ToString()
    {
        return BitConverter.ToString(GetBytes());
    }

    #endregion // ToString

    #region Equals

    /// <summary>
    /// Determines whether the current PacketStream is equal to another PacketStream.
    /// </summary>
    /// <param name="stream">The PacketStream to compare with.</param>
    /// <returns>True if the PacketStreams are equal; otherwise, false.</returns>
    public bool Equals(PacketStream stream)
    {
        if (Count != stream.Count)
            return false;

        for (var i = 0; i < Count; i++)
            if (this[i] != stream[i])
                return false;

        return true;
    }

    /// <summary>
    /// Determines whether the current PacketStream is equal to another object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if the objects are equal; otherwise, false.</returns>
    public override bool Equals(object obj)
    {
        if (obj is PacketStream stream)
            return Equals(stream);
        return false;
    }

    /// <summary>
    /// Gets the hash code for the PacketStream.
    /// </summary>
    /// <returns>The hash code for the PacketStream.</returns>
    public override int GetHashCode()
    {
        return Buffer.GetHashCode();
    }

    #endregion // Equals

    #region ICloneable Members

    /// <summary>
    /// Creates a shallow copy of the PacketStream.
    /// </summary>
    /// <returns>A shallow copy of the PacketStream.</returns>
    public object Clone()
    {
        return new PacketStream(this);
    }

    #endregion

    #region IComparable Members

    /// <summary>
    /// Compares the current PacketStream with another PacketStream.
    /// </summary>
    /// <param name="obj">The PacketStream to compare with.</param>
    /// <returns>A value indicating the relative order of the PacketStreams.</returns>
    public int CompareTo(object obj)
    {
        if (!(obj is PacketStream stream))
        {
            Logger.Error("Object is not a PacketStream instance");
            return 1; // Возвращаем значение по умолчанию
        }
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AAEmu.Commons.Conversion;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Utils;
using SBuffer = System.Buffer;

namespace AAEmu.Commons.Network;


/// <summary>
/// 用于管理、合并、读取和写入数据包的类。
/// 方法名称与 BinaryReader 和 BinaryWriter 中的名称相同。
/// → 类依赖于流的字节序！
/// </summary>
public class PacketStream : ICloneable, IComparable
{
    #region Data

    private const int DefaultSize = 128;

    #endregion // Data

    #region Properties

    /// <summary>
    /// 获取或（私有）设置包含流数据的底层字节数组。
    /// </summary>
    public byte[] Buffer { get; private set; }

    /// <summary>
    /// 获取或（私有）设置流中当前存储的字节数。
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// 获取底层缓冲区的总容量。
    /// </summary>
    public int Capacity => Buffer.Length;

    /// <summary>
    /// 获取或设置流中的当前读/写位置。
    /// </summary>
    public int Pos { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示流是否使用小端字节序。默认为 true。
    /// </summary>
    public bool IsLittleEndian { get; set; }
    /// <summary>
    /// 获取一个值，该值指示在当前位置之后是否还有可读取的字节。
    /// </summary>
    public bool HasBytes => Pos < Count;
    /// <summary>
    /// 获取从当前位置到流末尾剩余的字节数。
    /// </summary>
    public int LeftBytes => Count - Pos;

    /// <summary>
    /// 根据 <see cref="IsLittleEndian"/> 属性的值获取适当的 <see cref="EndianBitConverter"/>。
    /// </summary>
    public EndianBitConverter Converter =>
        (IsLittleEndian ? EndianBitConverter.Little : EndianBitConverter.Big);

    #endregion // Properties

    #region Operators & Casts

    /// <summary>
    /// 获取或设置位于流中指定索引处的字节。
    /// </summary>
    /// <param name="index">字节的索引。</param>
    /// <returns>指定索引处的字节。</returns>
    public byte this[int index]
    {
        set => Buffer[index] = value;
        get => Buffer[index];
    }

    /// <summary>
    /// 将字节数组显式转换为 <see cref="PacketStream"/>。
    /// </summary>
    /// <param name="o">要转换的字节数组。</param>
    public static explicit operator PacketStream(byte[] o)
    {
        return new PacketStream(o);
    }

    /// <summary>
    /// 将 <see cref="PacketStream"/> 隐式转换为字节数组。
    /// </summary>
    /// <param name="o">要转换的 <see cref="PacketStream"/>。</param>
    public static implicit operator byte[](PacketStream o)
    {
        return o.GetBytes();
    }

    #endregion // Operators & Casts

    #region Constructor

    /// <summary>
    /// 初始化 <see cref="PacketStream"/> 类的新实例，使用默认大小。
    /// 默认为小端字节序。
    /// </summary>
    public PacketStream() : this(DefaultSize)
    {
    }

    /// <summary>
    /// 初始化 <see cref="PacketStream"/> 类的新实例，使用指定的初始容量。
    /// 默认为小端字节序。
    /// </summary>
    /// <param name="count">流的初始容量。</param>
    public PacketStream(int count)
    {
        IsLittleEndian = true;
        Reserve(count);
    }

    /// <summary>
    /// 初始化 <see cref="PacketStream"/> 类的新实例，作为另一个 <see cref="PacketStream"/> 的副本。
    /// 字节序与源流相同。
    /// </summary>
    /// <param name="sourcePacketStream">要复制的源 <see cref="PacketStream"/>。</param>
    public PacketStream(PacketStream sourcePacketStream)
    {
        IsLittleEndian = sourcePacketStream.IsLittleEndian;
        Replace(sourcePacketStream);
    }

    /// <summary>
    /// 初始化 <see cref="PacketStream"/> 类的新实例，使用提供的字节数组。
    /// 默认为小端字节序。
    /// </summary>
    /// <param name="sourcebytes">用于初始化流的源字节数组。</param>
    public PacketStream(byte[] sourcebytes)
    {
        IsLittleEndian = true;
        Replace(sourcebytes);
    }

    /// <summary>
    /// 初始化 <see cref="PacketStream"/> 类的新实例，使用提供的字节数组的一部分。
    /// 默认为小端字节序。
    /// </summary>
    /// <param name="sourcebytes">包含要复制的数据的源字节数组。</param>
    /// <param name="offset">源字节数组中开始复制的偏移量。</param>
    /// <param name="count">要复制的字节数。</param>
    public PacketStream(byte[] sourcebytes, int offset, int count)
    {
        IsLittleEndian = true;
        Replace(sourcebytes, offset, count);
    }

    /// <summary>
    /// 初始化 <see cref="PacketStream"/> 类的新实例，使用另一个 <see cref="PacketStream"/> 的一部分。
    /// 字节序与源流相同。
    /// </summary>
    /// <param name="sourcePacketStream">包含要复制的数据的源 <see cref="PacketStream"/>。</param>
    /// <param name="offset">源流中开始复制的偏移量。</param>
    /// <param name="count">要复制的字节数。</param>
    public PacketStream(PacketStream sourcePacketStream, int offset, int count)
    {
        IsLittleEndian = sourcePacketStream.IsLittleEndian;
        Replace(sourcePacketStream, offset, count);
    }

    #endregion // Constructor

    #region Reserve & Roundup

    /// <summary>
    /// 将给定长度向上取整到下一个2的幂（最小为16）。
    /// 用于确定缓冲区分配的大小。
    /// </summary>
    /// <param name="length">要向上取整的长度。</param>
    /// <returns>向上取整后的长度。</returns>
    private static byte[] Roundup(int length)
    {
        var i = 16;
        while (length > i)
            i <<= 1;
        return new byte[i];
    }

    /// <summary>
    /// 使用提供的最小大小初始化此流的缓冲区。
    /// </summary>
    /// <param name="count">最小缓冲区大小。</param>
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
    /// 用提供的 PacketStream 替换当前的 PacketStream。
    /// </summary>
    /// <param name="stream">替换流。</param>
    /// <returns></returns>
    public PacketStream Replace(PacketStream stream)
    {
        return Replace(stream.Buffer, 0, stream.Count);
    }

    /// <summary>
    /// 用提供的字节数组替换当前的 PacketStream。
    /// </summary>
    /// <param name="bytes">字节数组</param>
    /// <returns></returns>
    public PacketStream Replace(byte[] bytes)
    {
        return Replace(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// 用提供流中的一些字节替换当前的 PacketStream。
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public PacketStream Replace(PacketStream stream, int offset, int count)
    {
        // 删除从 PacketStream 流复制后留下的垃圾数据
        return Replace(stream.Buffer, offset, count);
    }

    /// <summary>
    /// 用提供的字节数组中的一些字节替换当前的 PacketStream。
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
    /// 清除当前流。
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

    /// <summary>
    /// 将一个字节附加到流的末尾。
    /// </summary>
    /// <param name="b">要附加的字节。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream PushBack(byte b)
    {
        Reserve(Count + 1);
        Buffer[(Count++)] = b;
        return this;
    }

    #endregion // PushBack

    #region Swap

    /// <summary>
    /// 交换当前 <see cref="PacketStream"/> 与另一个 <see cref="PacketStream"/> 的内容。
    /// </summary>
    /// <param name="swapStream">要与之交换内容的 <see cref="PacketStream"/>。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
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
    /// 将当前读/写位置重置到流的开头 (索引 0)。
    /// </summary>
    public void Rollback()
    {
        Pos = 0;
    }

    /// <summary>
    /// 将当前读/写位置向后移动指定的长度。
    /// </summary>
    /// <param name="len">要向后移动的字节数。</param>
    public void Rollback(int len)
    {
        Pos -= len;
    }

    #endregion // Rollback

    #region Erase

    /// <summary>
    /// 从指定的起始位置擦除到流末尾的所有数据。
    /// </summary>
    /// <param name="from">开始擦除的索引（包含）。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Erase(int from)
    {
        return Erase(from, Count);
    }

    /// <summary>
    /// 从流中擦除指定范围的数据。
    /// </summary>
    /// <param name="from">开始擦除的索引（包含）。</param>
    /// <param name="to">结束擦除的索引（不包含）。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    /// <exception cref="ArgumentOutOfRangeException">如果 from 大于 to，或者 to 大于流的当前 Count。</exception>
    public PacketStream Erase(int from, int to)
    {
        if (from > to)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (Count < to)
            throw new ArgumentOutOfRangeException(nameof(to));

        // 移动有效内容以进行擦除
        SBuffer.BlockCopy(Buffer, to, Buffer, from, Count -= to - from);
        return this;
    }

    #endregion // Erase

    #region Insert

    /// <summary>
    /// 在指定的偏移量处插入另一个 <see cref="PacketStream"/> 的全部内容。
    /// </summary>
    /// <param name="offset">开始插入的索引。</param>
    /// <param name="copyStream">要插入的 <see cref="PacketStream"/>。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Insert(int offset, PacketStream copyStream)
    {
        return Insert(offset, copyStream.Buffer, 0, copyStream.Count);
    }

    /// <summary>
    /// 在指定的偏移量处插入一个字节数组的全部内容。
    /// </summary>
    /// <param name="offset">开始插入的索引。</param>
    /// <param name="copyArray">要插入的字节数组。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Insert(int offset, byte[] copyArray)
    {
        return Insert(offset, copyArray, 0, copyArray.Length);
    }

    /// <summary>
    /// 在指定的偏移量处插入另一个 <see cref="PacketStream"/> 的一部分内容。
    /// </summary>
    /// <param name="offset">当前流中开始插入的索引。</param>
    /// <param name="copyStream">要从中复制数据的源 <see cref="PacketStream"/>。</param>
    /// <param name="copyStreamOffset">源流中开始复制的偏移量。</param>
    /// <param name="count">要复制的字节数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Insert(int offset, PacketStream copyStream, int copyStreamOffset, int count)
    {
        return Insert(offset, copyStream.Buffer, copyStreamOffset, count);
    }

    /// <summary>
    /// 在指定的偏移量处插入一个字节数组的一部分内容。
    /// </summary>
    /// <param name="offset">当前流中开始插入的索引。</param>
    /// <param name="copyArray">要从中复制数据的源字节数组。</param>
    /// <param name="copyArrayOffset">源数组中开始复制的偏移量。</param>
    /// <param name="count">要复制的字节数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Insert(int offset, byte[] copyArray, int copyArrayOffset, int count)
    {
        Reserve(Count + count);
        // 将数据从位置 offset 移动到位置 offset + count
        SBuffer.BlockCopy(Buffer, offset, Buffer, offset + count, Count - offset);
        // 将新的数据数组复制到位置 offset
        SBuffer.BlockCopy(copyArray, copyArrayOffset, Buffer, offset, count);
        Count += count;
        return this;
    }

    #endregion // Insert

    #region GetBytes

    /// <summary>
    /// 获取包含流中当前所有数据的字节数组副本。
    /// </summary>
    /// <returns>包含流数据的字节数组。</returns>
    public byte[] GetBytes()
    {
        var temp = new byte[Count];
        SBuffer.BlockCopy(Buffer, 0, temp, 0, Count);
        return temp;
    }

    #endregion // GetBytes

    #region Read Primitive Types

    /// <summary>
    /// 从流中读取一个布尔值。1 表示 true，0 表示 false。
    /// </summary>
    /// <returns>读取的布尔值。</returns>
    public bool ReadBoolean()
    {
        return ReadByte() == 1;
    }

    /// <summary>
    /// 从流中读取一个字节。
    /// </summary>
    /// <returns>读取的字节。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public byte ReadByte()
    {
        if (Pos + 1 > Count)
            throw new MarshalException();
        return this[Pos++];
    }

    /// <summary>
    /// 从流中读取一个有符号字节。
    /// </summary>
    /// <returns>读取的有符号字节。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public sbyte ReadSByte()
    {
        if (Pos + 1 > Count)
            throw new MarshalException();
        return (sbyte)this[Pos++];
    }

    /// <summary>
    /// 从流中读取指定数量的字节。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    /// <returns>包含所读取字节的字节数组。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public byte[] ReadBytes(int count)
    {
        if (Pos + count > Count)
            throw new MarshalException();

        var result = new byte[count];
        SBuffer.BlockCopy(Buffer, Pos, result, 0, count);
        Pos += count;
        return result;
    }

    /// <summary>
    /// 从流中读取一个字节数组，其长度由流中的前两个字节（short类型）指定。
    /// </summary>
    /// <returns>包含所读取字节的字节数组。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取（包括长度或数据本身）。</exception>
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

    /// <summary>
    /// 从流中读取一个 Unicode 字符（2字节）。
    /// </summary>
    /// <returns>读取的字符。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public char ReadChar()
    {
        if (Pos + 2 > Count)
            throw new MarshalException();

        var result = Converter.ToChar(Buffer, Pos);
        Pos += 2;

        return result;
    }

    /// <summary>
    /// 从流中读取指定数量的 Unicode 字符。
    /// </summary>
    /// <param name="count">要读取的字符数。</param>
    /// <returns>包含所读取字符的字符数组。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public char[] ReadChars(int count)
    {
        if (Pos + 2 * count > Count)
            throw new MarshalException();

        var result = new char[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadChar();

        return result;
    }

    /// <summary>
    /// 从流中读取一个16位有符号整数。
    /// </summary>
    /// <returns>读取的16位有符号整数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public short ReadInt16()
    {
        if (Pos + 2 > Count)
            throw new MarshalException();

        var result = Converter.ToInt16(Buffer, Pos);
        Pos += 2;

        return result;
    }

    /// <summary>
    /// 从流中读取一个32位有符号整数。
    /// </summary>
    /// <returns>读取的32位有符号整数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public int ReadInt32()
    {
        if (Pos + 4 > Count)
            throw new MarshalException();

        var result = Converter.ToInt32(Buffer, Pos);
        Pos += 4;

        return result;
    }

    /// <summary>
    /// 从流中读取一个64位有符号整数。
    /// </summary>
    /// <returns>读取的64位有符号整数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public long ReadInt64()
    {
        if (Pos + 8 > Count)
            throw new MarshalException();

        var result = Converter.ToInt64(Buffer, Pos);
        Pos += 8;

        return result;
    }

    /// <summary>
    /// 从流中读取一个16位无符号整数。
    /// </summary>
    /// <returns>读取的16位无符号整数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public ushort ReadUInt16()
    {
        if (Pos + 2 > Count)
            throw new MarshalException();

        var result = Converter.ToUInt16(Buffer, Pos);
        Pos += 2;

        return result;
    }

    /// <summary>
    /// 从流中读取一个32位无符号整数。
    /// </summary>
    /// <returns>读取的32位无符号整数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public uint ReadUInt32()
    {
        if (Pos + 4 > Count)
            throw new MarshalException();

        var result = Converter.ToUInt32(Buffer, Pos);
        Pos += 4;

        return result;
    }

    /// <summary>
    /// 从流中读取一个3字节的无符号整数 (Big Endian 风格，常用于特定协议)。
    /// </summary>
    /// <returns>读取的3字节无符号整数，扩展为 uint。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public uint ReadBc()
    {
        if (Pos + 3 > Count)
            throw new MarshalException();

        var result = ReadUInt16() + (ReadByte() << 16);

        return (uint)result;
    }

    /// <summary>
    /// 从流中读取一个64位无符号整数。
    /// </summary>
    /// <returns>读取的64位无符号整数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public ulong ReadUInt64()
    {
        if (Pos + 8 > Count)
            throw new MarshalException();

        var result = Converter.ToUInt64(Buffer, Pos);
        Pos += 8;

        return result;
    }

    /// <summary>
    /// 从流中读取一个单精度浮点数。
    /// </summary>
    /// <returns>读取的单精度浮点数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public float ReadSingle()
    {
        if (Pos + 4 > Count)
            throw new MarshalException();

        var result = Converter.ToSingle(Buffer, Pos);
        Pos += 4;

        return result;
    }

    /// <summary>
    /// 从流中读取一个双精度浮点数。
    /// </summary>
    /// <returns>读取的双精度浮点数。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
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

    /// <summary>
    /// 从当前流中读取数据，创建一个新的 <see cref="PacketStream"/> 实例。
    /// 新流的长度由当前流中的一个16位整数指定。
    /// </summary>
    /// <returns>包含所读取数据的新 <see cref="PacketStream"/> 实例。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public PacketStream ReadPacketStream()
    {
        var i = ReadInt16();
        if (Pos + i > Count)
            throw new MarshalException();
        var newStream = new PacketStream(Buffer, Pos, i);
        Pos += i;
        return newStream;
    }

    /// <summary>
    /// 从当前流中读取数据，并用其替换提供的 <see cref="PacketStream"/> 的内容。
    /// 要读取的数据长度由当前流中的一个16位整数指定。
    /// </summary>
    /// <param name="stream">要用读取的数据替换其内容的 <see cref="PacketStream"/>。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public PacketStream Read(PacketStream stream)
    {
        var i = ReadInt16();
        if (Pos + i > Count)
            throw new MarshalException();
        stream.Replace(Buffer, Pos, i);
        Pos += i;
        return this;
    }

    /// <summary>
    /// 从当前流中读取数据，以填充提供的 <see cref="PacketMarshaler"/> 对象。
    /// </summary>
    /// <param name="paramMarshal">要用读取的数据填充的 <see cref="PacketMarshaler"/> 对象。</param>
    public void Read(PacketMarshaler paramMarshal)
    {
        paramMarshal.Read(this);
    }

    /// <summary>
    /// 从当前流中读取数据，并创建一个新的指定类型 <typeparamref name="T"/> 的 <see cref="PacketMarshaler"/> 对象。
    /// </summary>
    /// <typeparam name="T">要创建和填充的对象的类型，必须是 <see cref="PacketMarshaler"/> 并具有无参构造函数。</typeparam>
    /// <returns>已填充数据的新的 <typeparamref name="T"/> 类型对象。</returns>
    public T Read<T>() where T : PacketMarshaler, new()
    {
        var t = new T();
        Read(t);
        return t;
    }

    /// <summary>
    /// 从当前流中读取一个 <see cref="PacketMarshaler"/> 对象集合。
    /// 集合中对象的数量由流中的一个32位整数指定。
    /// </summary>
    /// <typeparam name="T">集合中对象的类型，必须是 <see cref="PacketMarshaler"/> 并具有无参构造函数。</typeparam>
    /// <returns>包含已读取对象的 <see cref="List{T}"/>。</returns>
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

    /// <summary>
    /// 从流中读取一个表示 Unix 时间戳 (秒) 的64位整数，并将其转换为 <see cref="DateTime"/> 对象。
    /// </summary>
    /// <returns>转换后的 <see cref="DateTime"/> 对象。</returns>
    public DateTime ReadDateTime()
    {
        return Helpers.UnixTime(ReadInt64());
    }

    /// <summary>
    /// 从流中读取一个压缩整数数组 (PISC - Packed Integer SCaled)。
    /// 数组的每个元素根据其大小使用不同数量的字节进行编码。
    /// 第一个字节是一个位掩码，指示后续每个元素的大小。
    /// </summary>
    /// <param name="count">要读取的整数数量。</param>
    /// <returns>包含解压缩整数的 long 数组。</returns>
    public long[] ReadPisc(int count)
    {
        var result = new long[count];
        var pish = new BitArray(new byte[] { ReadByte() }); // pish 字节决定后续每个整数的大小
        for (var index = 0; index < count * 2; index += 2)
        {
            if (pish[index] && pish[index + 1]) // 两位都为1: uint (4字节)
                result[index / 2] = ReadUInt32();
            else if (pish[index + 1]) // 第二位为1: bc (3字节)
                result[index / 2] = ReadBc();
            else if (pish[index]) // 第一位为1: ushort (2字节)
                result[index / 2] = ReadUInt16();
            else // 两位都为0: byte (1字节)
                result[index / 2] = ReadByte();
        }

        return result;
    }

    /// <summary>
    /// 从流中读取9个字节并将其转换为表示三维坐标 (x, y, z) 的元组。
    /// </summary>
    /// <returns>包含 x, y, z 坐标的元组。</returns>
    public (float x, float y, float z) ReadPosition()
    {
        var position = ReadBytes(9);
        return Helpers.ConvertPosition(position);
    }

    /// <summary>
    /// 从流中读取三个16位整数，并将它们转换为一个压缩表示的 <see cref="Quaternion"/>。
    /// W分量根据 X, Y, Z 分量计算得出，以确保四元数的范数接近1。
    /// </summary>
    /// <returns>解压缩后的 <see cref="Quaternion"/>。</returns>
    public Quaternion ReadQuaternionShort()
    {
        var quatX = Convert.ToSingle(ReadInt16() * 0.000030518509f); // 将 short 转换为 [-1, 1] 范围内的 float
        var quatY = Convert.ToSingle(ReadInt16() * 0.000030518509f);
        var quatZ = Convert.ToSingle(ReadInt16() * 0.000030518509f);
        var quatNorm = quatX * quatX + quatY * quatY + quatZ * quatZ;

        var quatW = 0.0f;
        if (quatNorm < 0.99750) // 阈值用于确定是否需要计算 W
        {
            quatW = (float)Math.Sqrt(1.0 - quatNorm);
        }

        var quat = new Quaternion(quatX, quatY, quatZ, quatW);

        return quat;
    }

    /// <summary>
    /// 从流中读取三个单精度浮点数，并将其组合成一个 <see cref="Vector3"/>。
    /// </summary>
    /// <returns>读取的 <see cref="Vector3"/>。</returns>
    public Vector3 ReadVector3Single()
    {
        var x = ReadSingle();
        var y = ReadSingle();
        var z = ReadSingle();
        var temp = new Vector3(x, y, z);
        return temp;
    }

    /// <summary>
    /// 从流中读取三个16位整数，并将它们转换为一个压缩表示的 <see cref="Vector3"/>。
    /// 每个分量都通过乘以一个缩放因子从 short 转换为 float。
    /// </summary>
    /// <returns>解压缩后的 <see cref="Vector3"/>。</returns>
    public Vector3 ReadVector3Short()
    {
        var x = Convert.ToSingle(ReadInt16()) * 0.000030518509f; // 将 short 转换为 [-1, 1] 范围内的 float (近似)
        var y = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
        var z = Convert.ToSingle(ReadInt16()) * 0.000030518509f;
        var temp = new Vector3(x, y, z);

        return temp;
    }

    #endregion // Read Complex Types

    #region Read Strings

    /// <summary>
    /// 从流中读取一个字符串。字符串的长度由流中的前两个字节（short类型）指定，
    /// 然后读取相应数量的字节并将其解码为 UTF-8 字符串。
    /// </summary>
    /// <returns>读取的字符串，已移除末尾的空字符。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public string ReadString()
    {
        var i = ReadInt16();
        var strBuf = ReadBytes(i);
        return Encoding.UTF8.GetString(strBuf).Trim('\u0000');
    }

    /// <summary>
    /// 从流中读取指定长度的字符串。
    /// 读取指定数量的字节并将其解码为 UTF-8 字符串。
    /// </summary>
    /// <param name="len">要读取的字节数（字符串的编码后长度）。</param>
    /// <returns>读取的字符串，已移除末尾的空字符。</returns>
    /// <exception cref="MarshalException">如果流中没有足够的字节可供读取。</exception>
    public string ReadString(int len)
    {
        var strBuf = ReadBytes(len);
        return Encoding.UTF8.GetString(strBuf).Trim('\u0000');
    }

    #endregion // Read Strings

    #region Write Primitive Types

    /// <summary>
    /// 将一个布尔值写入流。true 写入为 0x01，false 写入为 0x00。
    /// </summary>
    /// <param name="value">要写入的布尔值。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(bool value)
    {
        return Write(value ? (byte)0x01 : (byte)0x00);
    }

    /// <summary>
    /// 将一个字节写入流。
    /// </summary>
    /// <param name="value">要写入的字节。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(byte value)
    {
        PushBack(value);
        return this;
    }

    /// <summary>
    /// 将一个字节数组写入流。
    /// </summary>
    /// <param name="value">要写入的字节数组。</param>
    /// <param name="appendSize">如果为 true，则首先将数组的长度（作为 ushort）写入流。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(byte[] value, bool appendSize = false)
    {
        if (appendSize)
            Write((ushort)value.Length);
        return Insert(Count, value);
    }

    /// <summary>
    /// 将一个有符号字节写入流。
    /// </summary>
    /// <param name="value">要写入的有符号字节。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(sbyte value)
    {
        return Write((byte)value);
    }

    /// <summary>
    /// 将一个 Unicode 字符（2字节）写入流。
    /// </summary>
    /// <param name="value">要写入的字符。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(char value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个字符数组写入流。
    /// </summary>
    /// <param name="value">要写入的字符数组。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(char[] value)
    {
        foreach (var ch in value)
            Write(ch);
        return this;
    }

    /// <summary>
    /// 将一个16位有符号整数写入流。
    /// </summary>
    /// <param name="value">要写入的16位有符号整数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(short value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个32位有符号整数写入流。
    /// </summary>
    /// <param name="value">要写入的32位有符号整数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(int value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个64位有符号整数写入流。
    /// </summary>
    /// <param name="value">要写入的64位有符号整数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(long value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个16位无符号整数写入流。
    /// </summary>
    /// <param name="value">要写入的16位无符号整数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(ushort value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个32位无符号整数写入流。
    /// </summary>
    /// <param name="value">要写入的32位无符号整数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(uint value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个64位无符号整数写入流。
    /// </summary>
    /// <param name="value">要写入的64位无符号整数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(ulong value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个单精度浮点数写入流。
    /// </summary>
    /// <param name="value">要写入的单精度浮点数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(float value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个双精度浮点数写入流。
    /// </summary>
    /// <param name="value">要写入的双精度浮点数。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(double value)
    {
        return Write(Converter.GetBytes(value));
    }

    /// <summary>
    /// 将一个3字节的无符号整数写入流 (Big Endian 风格)。
    /// </summary>
    /// <param name="value">要写入的 uint 值（仅使用低3字节）。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WriteBc(uint value)
    {
        return Write(Converter.GetBytes(value, 3));
    }

    #endregion // Write Primitive Types

    #region Write Complex Types

    /// <summary>
    /// 将一个 <see cref="PacketMarshaler"/> 对象写入流。
    /// </summary>
    /// <param name="value">要写入的 <see cref="PacketMarshaler"/> 对象。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(PacketMarshaler value)
    {
        return value.Write(this);
    }

    /// <summary>
    /// 将一个 <see cref="PacketMarshaler"/> 对象集合写入流。
    /// 首先写入集合中对象的数量（作为32位整数），然后逐个写入每个对象。
    /// </summary>
    /// <typeparam name="T">集合中对象的类型，必须是 <see cref="PacketMarshaler"/>。</typeparam>
    /// <param name="values">要写入的对象集合。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write<T>(ICollection<T> values) where T : PacketMarshaler
    {
        Write(values.Count);
        foreach (var marshaler in values)
            Write(marshaler);
        return this;
    }

    /// <summary>
    /// 将另一个 <see cref="PacketStream"/> 的内容写入当前流。
    /// </summary>
    /// <param name="value">要写入的 <see cref="PacketStream"/>。</param>
    /// <param name="appendSize">如果为 true，则首先将源流的长度（作为 ushort）写入当前流。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(PacketStream value, bool appendSize = true)
    {
        return Write(value.GetBytes(), appendSize);
    }

    /// <summary>
    /// 将 <see cref="DateTime"/> 对象作为 Unix 时间戳 (秒) 写入流。
    /// </summary>
    /// <param name="value">要写入的 <see cref="DateTime"/> 对象。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(DateTime value)
    {
        return Write(Helpers.UnixTime(value));
    }

    /// <summary>
    /// 将 <see cref="Guid"/> 对象作为字节数组写入流。
    /// </summary>
    /// <param name="value">要写入的 <see cref="Guid"/> 对象。</param>
    /// <param name="appendSize">如果为 true，则首先将 Guid 字节数组的长度（通常为16）作为 ushort 写入流。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(Guid value, bool appendSize = true)
    {
        return Write(value.ToByteArray(), appendSize);
    }

    /// <summary>
    /// 将一个 long 数组作为压缩整数 (PISC) 写入流。
    /// 首先写入一个字节的位掩码，指示后续每个整数的大小，然后写入压缩后的整数。
    /// </summary>
    /// <param name="values">要写入的 long 数组。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WritePisc(params long[] values)
    {
        var pish = new BitArray(8); // 位掩码，最多支持4个整数（每个整数2位）
        var temp = new PacketStream(); // 临时流用于存储压缩后的整数
        var index = 0;
        foreach (var value in values)
        {
            if (value <= byte.MaxValue) // 1字节
                temp.Write((byte)value);
            else if (value <= ushort.MaxValue) // 2字节
            {
                pish[index] = true; // 设置第一位
                temp.Write((ushort)value);
            }
            else if (value <= 0xffffff) // 3字节 (bc)
            {
                pish[index + 1] = true; // 设置第二位
                temp.WriteBc((uint)value);
            }
            else // 4字节 (uint)
            {
                pish[index] = true;     // 设置第一位
                pish[index + 1] = true; // 设置第二位
                temp.Write((uint)value);
            }

            index += 2;
        }

        var res = new byte[1];
        pish.CopyTo(res, 0); // 将位掩码复制到字节数组
        Write(res[0]);       // 写入位掩码
        Write(temp, false);  // 写入压缩后的整数数据，不附加长度
        return this;
    }

    /// <summary>
    /// 将三维坐标 (x, y, z) 转换为9字节的表示形式并写入流。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    /// <param name="z">Z 坐标。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WritePosition(float x, float y, float z)
    {
        var res = Helpers.ConvertPosition(x, y, z);
        Write(res);
        return this;
    }

    /// <summary>
    /// 将 <see cref="Vector3"/> 对象转换为9字节的表示形式并写入流。
    /// </summary>
    /// <param name="pos">要写入的 <see cref="Vector3"/> 对象。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WritePosition(Vector3 pos)
    {
        var res = Helpers.ConvertPosition(pos.X, pos.Y, pos.Z);
        Write(res);
        return this;
    }

    /// <summary>
    /// 将 <see cref="Quaternion"/> 对象以压缩形式（3个或4个 short）写入流。
    /// </summary>
    /// <param name="values">要写入的 <see cref="Quaternion"/>。</param>
    /// <param name="scalar">如果为 true，则也写入 W 分量；否则仅写入 X, Y, Z 分量。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WriteQuaternionShort(Quaternion values, bool scalar = false)
    {
        var temp = new PacketStream();
        try
        {
            temp.Write(Convert.ToInt16(values.X * 32767f)); // 将 float 分量 [-1,1] 转换为 short
            temp.Write(Convert.ToInt16(values.Y * 32767f));
            temp.Write(Convert.ToInt16(values.Z * 32767f));
        }
        catch // 捕获可能的转换异常（例如，如果值超出short范围）
        {
            var res = new byte[6]; // 写入零值作为回退
            temp.Write(res);
        }

        if (scalar)
        {
            temp.Write(Convert.ToInt16(values.W));
        }
        return Write(temp, false); // 不附加长度信息
    }

    /// <summary>
    /// 将 <see cref="Vector3"/> 对象的每个分量作为单精度浮点数写入流。
    /// </summary>
    /// <param name="values">要写入的 <see cref="Vector3"/>。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WriteVector3Single(Vector3 values)
    {
        var temp = new PacketStream();
        temp.Write(values.X);
        temp.Write(values.Y);
        temp.Write(values.Z);
        return Write(temp, false);
    }

    /// <summary>
    /// 将 <see cref="Vector3"/> 对象以压缩形式（每个分量一个 short）写入流。
    /// </summary>
    /// <param name="values">要写入的 <see cref="Vector3"/>。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream WriteVector3Short(Vector3 values)
    {
        var temp = new PacketStream();
        temp.Write(Convert.ToInt16(values.X * 32767f)); // 将 float 分量转换为 short
        temp.Write(Convert.ToInt16(values.Y * 32767f));
        temp.Write(Convert.ToInt16(values.Z * 32767f));
        return Write(temp, false);
    }

    #endregion // Write Complex Types

    #region Write Strings

    /// <summary>
    /// 将一个字符串写入流。
    /// </summary>
    /// <param name="value">要写入的字符串。</param>
    /// <param name="appendSize">如果为 true，则首先将字符串的编码后长度（作为 ushort）写入流。</param>
    /// <param name="appendTerminator">如果为 true，则在字符串末尾追加一个空字符 ('\u0000') 后再进行编码。</param>
    /// <returns>当前的 <see cref="PacketStream"/> 实例。</returns>
    public PacketStream Write(string value, bool appendSize = true, bool appendTerminator = false)
    {
        var str = Encoding.UTF8.GetBytes(appendTerminator ? value + '\u0000' : value); // utf-8 编码
        return Write(str, appendSize);
    }

    #endregion // Write Strings

    #region ToString

    /// <summary>
    /// 返回表示当前对象所含数据（字节）的字符串。
    /// 字节以十六进制形式表示，并由破折号分隔。
    /// </summary>
    /// <returns>当前流内容的十六进制字符串表示形式。</returns>
    public override string ToString()
    {
        return BitConverter.ToString(GetBytes());
    }

    #endregion // ToString

    #region Equals

    /// <summary>
    /// 确定当前的 <see cref="PacketStream"/> 是否等于另一个 <see cref="PacketStream"/>。
    /// 比较基于流中字节内容的逐字节比较。
    /// </summary>
    /// <param name="stream">要与当前流进行比较的 <see cref="PacketStream"/>。</param>
    /// <returns>如果两个流的内容相同，则为 true；否则为 false。</returns>
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
    /// 确定当前对象是否等于另一个对象。
    /// </summary>
    /// <param name="obj">要与当前对象进行比较的对象。</param>
    /// <returns>如果 obj 是 <see cref="PacketStream"/> 并且其内容与当前实例相同，则为 true；否则为 false。</returns>
    public override bool Equals(object obj)
    {
        if (obj is PacketStream stream)
            return Equals(stream);
        return false;
    }

    /// <summary>
    /// 返回此实例的哈希码。
    /// </summary>
    /// <returns>底层缓冲区的哈希码。</returns>
    public override int GetHashCode()
    {
        return Buffer.GetHashCode();
    }

    #endregion // Equals

    #region ICloneable Members

    /// <summary>
    /// 创建作为当前实例副本的新对象。
    /// </summary>
    /// <returns>作为此实例副本的新 <see cref="PacketStream"/> 对象。</returns>
    public object Clone()
    {
        return new PacketStream(this);
    }

    #endregion

    #region IComparable Members

    /// <summary>
    /// 将当前实例与另一个对象进行比较，并返回一个整数，该整数指示当前实例在排序顺序中是位于另一个对象之前、之后还是与其出现在相同位置。
    /// 比较基于流内容的逐字节比较。
    /// </summary>
    /// <param name="obj">要与此实例进行比较的对象。</param>
    /// <returns>
    /// 一个值，指示所比较对象的相对顺序。
    /// 返回值的含义如下：
    /// 小于零：此实例在排序顺序中位于 obj 之前。
    /// 零：此实例在排序顺序中与 obj 出现在相同位置。
    /// 大于零：此实例在排序顺序中位于 obj 之后。
    /// </returns>
    /// <exception cref="ArgumentException">obj 不是 PacketStream 实例。</exception>
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

using CgfConverter;
using CgfConverter.Structs;
using CgfConverter.Utililities;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Extensions;

// This includes changes for 2.6 created by Dymek (byte4/1/2hex, and 20 byte per element vertices).  Thank you!
public static class BinaryReaderExtensions
{
    public static float ReadCryHalf(this BinaryReader r)
    {
        // See CryHalf.inl in the Lumberyard project.  Stored as uint16.
        var bver = r.ReadUInt16();

        return CryHalf.ConvertCryHalfToFloat(bver);
    }

    public static float ReadDymekHalf(this BinaryReader r)
    {
        var bver = r.ReadUInt16();

        return CryHalf.ConvertDymekHalfToFloat(bver);
    }

    public static Plane ReadPlane(this BinaryReader r)
    {
        var n = r.ReadVector3();
        var d = r.ReadSingle();
        return new Plane(n, d);
    }

    public static Vector3 ReadVector3(this BinaryReader r, InputType inputType = InputType.Single)
    {
        Vector3 v;
        switch (inputType)
        {
            case InputType.Single:
                v = new()
                {
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Z = r.ReadSingle()
                };
                break;
            case InputType.Half:
                v = new()
                {
                    X = (float)r.ReadHalf(),
                    Y = (float)r.ReadHalf(),
                    Z = (float)r.ReadHalf()
                };
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return v;
    }

    public static Quaternion ReadQuaternion(this BinaryReader r, InputType inputType = InputType.Single)
    {
        Quaternion q;

        switch (inputType)
        {
            case InputType.Single:
                q = new Quaternion()
                {
                    X = r.ReadSingle(),
                    Y = r.ReadSingle(),
                    Z = r.ReadSingle(),
                    W = r.ReadSingle()
                };
                break;
            case InputType.Half:
                q = new Quaternion()
                {
                    X = (float)r.ReadHalf(),
                    Y = (float)r.ReadHalf(),
                    Z = (float)r.ReadHalf(),
                    W = (float)r.ReadHalf()
                };
                break;
            default:
                throw new ArgumentOutOfRangeException("Unable to read Quaternion.");
        }

        return q;
    }

    public static AaBb ReadAaBb(this BinaryReader reader)
    {
        return new AaBb
        {
            Min = reader.ReadVector3(),
            Max = reader.ReadVector3(),
        };
    }

    public static ShotInt3Quat ReadShotInt3Quat(this BinaryReader r)
    {
        return new ShotInt3Quat
        {
            X = r.ReadInt16(),
            Y = r.ReadInt16(),
            Z = r.ReadInt16(),
        };
    }

    public static SmallTreeDWORDQuat ReadSmallTreeDWORDQuat(this BinaryReader r)
    {
        return new SmallTreeDWORDQuat
        {
            Value = r.ReadUInt32(),
        };
    }

    public static SmallTree48BitQuat ReadSmallTree48BitQuat(this BinaryReader r)
    {
        return new SmallTree48BitQuat
        {
            M1 = r.ReadUInt16(),
            M2 = r.ReadUInt16(),
            M3 = r.ReadUInt16(),
        };
    }

    public static SmallTree64BitQuat ReadSmallTree64BitQuat(this BinaryReader r)
    {
        return new SmallTree64BitQuat
        {
            M1 = r.ReadUInt32(),
            M2 = r.ReadUInt32(),
        };
    }

    public static SmallTree64BitExtQuat ReadSmallTree64BitExtQuat(this BinaryReader r)
    {
        return new SmallTree64BitExtQuat
        {
            M1 = r.ReadUInt32(),
            M2 = r.ReadUInt32(),
        };
    }

    public static IRGBA ReadColor(this BinaryReader r)
    {
        var c = new IRGBA()
        {
            r = r.ReadByte(),
            g = r.ReadByte(),
            b = r.ReadByte(),
            a = r.ReadByte()
        };
        return c;
    }

    public static IRGBA ReadColorBGRA(this BinaryReader r)
    {
        var c = new IRGBA()
        {
            b = r.ReadByte(),
            g = r.ReadByte(),
            r = r.ReadByte(),
            a = r.ReadByte()
        };
        return c;
    }

    public static Matrix3x3 ReadMatrix3x3(this BinaryReader reader)
    {
        // Reads a Matrix33 structure
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        Matrix3x3 m = new()
        {
            M11 = reader.ReadSingle(),
            M12 = reader.ReadSingle(),
            M13 = reader.ReadSingle(),
            M21 = reader.ReadSingle(),
            M22 = reader.ReadSingle(),
            M23 = reader.ReadSingle(),
            M31 = reader.ReadSingle(),
            M32 = reader.ReadSingle(),
            M33 = reader.ReadSingle()
        };

        return m;
    }

    public static Matrix3x4 ReadMatrix3x4(this BinaryReader r)
    {
        if (r == null)
            throw new ArgumentNullException(nameof(r));

        Matrix3x4 m = new()
        {
            M11 = r.ReadSingle(),
            M12 = r.ReadSingle(),
            M13 = r.ReadSingle(),
            M14 = r.ReadSingle(),
            M21 = r.ReadSingle(),
            M22 = r.ReadSingle(),
            M23 = r.ReadSingle(),
            M24 = r.ReadSingle(),
            M31 = r.ReadSingle(),
            M32 = r.ReadSingle(),
            M33 = r.ReadSingle(),
            M34 = r.ReadSingle()
        };

        return m;
    }

    public static Matrix4x4 ReadMatrix4x4(this BinaryReader r)
    {
        if (r == null)
            throw new ArgumentNullException(nameof(r));

        Matrix4x4 m = new()
        {
            M11 = r.ReadSingle(),
            M12 = r.ReadSingle(),
            M13 = r.ReadSingle(),
            M14 = r.ReadSingle(),
            M21 = r.ReadSingle(),
            M22 = r.ReadSingle(),
            M23 = r.ReadSingle(),
            M24 = r.ReadSingle(),
            M31 = r.ReadSingle(),
            M32 = r.ReadSingle(),
            M33 = r.ReadSingle(),
            M34 = r.ReadSingle(),
            M41 = r.ReadSingle(),
            M42 = r.ReadSingle(),
            M43 = r.ReadSingle(),
            M44 = r.ReadSingle()
        };

        return m;
    }

    public enum InputType
    {
        Half,
        CryHalf,
        Single,
        Double
    }
    
    public static int ReadCryInt(this Stream stream)
    {
        var current = stream.ReadByte();
        var result = current & 0x7F;
        while ((current & 0x80) != 0)
        {
            current = stream.ReadByte();
            result = (result << 7) | (current & 0x7F);
        }

        return result;
    }

    public static int ReadCryInt(this BinaryReader reader) => ReadCryInt(reader.BaseStream);

    public static int ReadCryIntWithFlag(this Stream stream, out bool flag)
    {
        var current = stream.ReadByte();
        var result = current & 0x3F;
        flag = (current & 0x40) != 0;
        while ((current & 0x80) != 0)
        {
            current = stream.ReadByte();
            result = (result << 7) | (current & 0x7F);
        }

        return result;
    }

    public static int ReadCryIntWithFlag(this BinaryReader reader, out bool flag) => ReadCryIntWithFlag(reader.BaseStream, out flag);

    public static unsafe T ReadEnum<T>(this BinaryReader reader) where T : unmanaged, Enum
    {
        switch (Marshal.SizeOf(Enum.GetUnderlyingType(typeof(T))))
        {
            case 1:
                var b1 = reader.ReadByte();
                return *(T*) &b1;
            case 2:
                var b2 = reader.ReadUInt16();
                return *(T*) &b2;
            case 4:
                var b4 = reader.ReadUInt32();
                return *(T*) &b4;
            case 8:
                var b8 = reader.ReadUInt64();
                return *(T*) &b8;
            default:
                throw new ArgumentException("Enum is not of size 1, 2, 4, or 8.", nameof(T), null);
        }
    }
    
    public static void ReadInto(this BinaryReader reader, out byte value) => value = reader.ReadByte();
    public static void ReadInto(this BinaryReader reader, out sbyte value) => value = reader.ReadSByte();
    public static void ReadInto(this BinaryReader reader, out ushort value) => value = reader.ReadUInt16();
    public static void ReadInto(this BinaryReader reader, out short value) => value = reader.ReadInt16();
    public static void ReadInto(this BinaryReader reader, out uint value) => value = reader.ReadUInt32();
    public static void ReadInto(this BinaryReader reader, out int value) => value = reader.ReadInt32();
    public static void ReadInto(this BinaryReader reader, out ulong value) => value = reader.ReadUInt64();
    public static void ReadInto(this BinaryReader reader, out long value) => value = reader.ReadInt64();
    public static void ReadInto(this BinaryReader reader, out float value) => value = reader.ReadSingle();
    public static void ReadInto(this BinaryReader reader, out double value) => value = reader.ReadDouble();
    
    public static void ReadInto<T>(this BinaryReader reader, out T value) where T : unmanaged, Enum
        => value = reader.ReadEnum<T>();

    public static void ReadInto(this BinaryReader reader, out AaBb value) => value = reader.ReadAaBb();
    public static void ReadInto(this BinaryReader reader, out Plane value) => value = reader.ReadPlane();
    public static void ReadInto(this BinaryReader reader, out Vector3 value) => value = reader.ReadVector3();
    public static void ReadInto(this BinaryReader reader, out Matrix3x4 value) => value = reader.ReadMatrix3x4();
    public static void ReadInto(this BinaryReader reader, out Matrix3x3 value) => value = reader.ReadMatrix3x3();

    public static void AlignTo(this BinaryReader reader, int unit) =>
        reader.BaseStream.Position = (reader.BaseStream.Position + unit - 1) / unit * unit;
}

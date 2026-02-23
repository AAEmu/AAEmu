using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Hashing;
using System.Text;
using CgfConverter.Services;
using Extensions;

namespace CgfConverter.CryEngineCore;

public class ChunkGlobalAnimationHeaderCAF_971 : ChunkGlobalAnimationHeaderCAF
{
    public override void Read(BinaryReader reader)
    {
        base.Read(reader);

        ((EndiannessChangeableBinaryReader) reader).IsBigEndian = false;
        
        Flags = reader.ReadUInt32();
        FilePath = reader.ReadFString(256);
        FilePathCRC32 = reader.ReadUInt32();

        var crc32 = BinaryPrimitives.ReadUInt32LittleEndian(Crc32.Hash(Encoding.UTF8.GetBytes(FilePath)));
        if (crc32 == FilePathCRC32)
        {
            // do nothing
        }
        else if (BinaryPrimitives.ReverseEndianness(crc32) == FilePathCRC32)
        {
            ((EndiannessChangeableBinaryReader) reader).IsBigEndian = true;
            Flags = BinaryPrimitives.ReverseEndianness(Flags);
            FilePathCRC32 = crc32;
        }
        else
        {
            throw new Exception("Invalid FilePathCRC32");
        }
        FilePathDBACRC32 = reader.ReadUInt32();
        
        LHeelStart = reader.ReadSingle();
        LHeelEnd = reader.ReadSingle();
        LToe0Start = reader.ReadSingle();
        LToe0End = reader.ReadSingle();
        RHeelStart = reader.ReadSingle();
        RHeelEnd = reader.ReadSingle();
        RToe0Start = reader.ReadSingle();
        RToe0End = reader.ReadSingle();
        
        StartSec = reader.ReadSingle();
        EndSec = reader.ReadSingle();
        TotalDuration = reader.ReadSingle();
        Controllers = reader.ReadUInt32();
        
        StartLocation = reader.ReadQuaternion();
        LastLocatorKey = reader.ReadQuaternion();
        Velocity = reader.ReadVector3();
        Distance = reader.ReadSingle();
        Speed = reader.ReadSingle();
        Slope = reader.ReadSingle();
        TurnSpeed = reader.ReadSingle();
        AssetTurn = reader.ReadSingle();
    }
}
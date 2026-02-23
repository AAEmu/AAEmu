using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CgfConverter.Services;
using CgfConverter.Utils;
using Extensions;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkController_905 : ChunkController
{
    private readonly TaggedLogger Log = new TaggedLogger(nameof(ChunkController_905));

    public uint NumKeyPos { get; internal set; }
    public uint NumKeyRot { get; internal set; }
    public uint NumKeyTime { get; internal set; }
    public uint NumAnims { get; internal set; }

    public List<List<float>>? KeyTimes { get; internal set; }
    public List<List<Vector3>>? KeyPositions { get; internal set; }
    public List<List<Quaternion>>? KeyRotations { get; internal set; }
    public List<Animation>? Animations { get; internal set; }

    public override void Read(BinaryReader b)
    {
        base.Read(b);

        // Assume little endian for now
        ((EndiannessChangeableBinaryReader) b).IsBigEndian = false;

        NumKeyPos = b.ReadUInt32();
        NumKeyRot = b.ReadUInt32();
        NumKeyTime = b.ReadUInt32();
        NumAnims = b.ReadUInt32();

        // Test: if there exists a number that exceeds 0x10000, it may not be in little endian.
        var endianCheck =
            (NumKeyPos >= 0x10000 ? 1 : 0) |
            (NumKeyRot >= 0x10000 ? 2 : 0) |
            (NumKeyTime >= 0x10000 ? 4 : 0) |
            (NumAnims >= 0x10000 ? 8 : 0);

        switch (endianCheck)
        {
            case 0: // most likely to be little endian
                break; // do nothing

            case 15: // most likely to be big endian
                NumKeyPos = BinaryPrimitives.ReverseEndianness(NumKeyPos);
                NumKeyRot = BinaryPrimitives.ReverseEndianness(NumKeyRot);
                NumKeyTime = BinaryPrimitives.ReverseEndianness(NumKeyTime);
                NumAnims = BinaryPrimitives.ReverseEndianness(NumAnims);
                ((EndiannessChangeableBinaryReader) b).IsBigEndian = true;
                Log.I($"Assuming big endian: {this}");
                break;

            default:
                Log.W($"Could not deduce endianness(endianCheck={endianCheck}); assuming little endian: {this}");
                break;
        }

        var keyTimeLengths = Enumerable.Range(0, (int) NumKeyTime).Select(_ => b.ReadUInt16()).ToList();
        var keyTimeFormats = Enumerable.Range(0, (int) EKeyTimesFormat.eBitset + 1).Select(_ => b.ReadUInt32()).ToList();
        
        var keyPosLengths = Enumerable.Range(0, (int) NumKeyPos).Select(_ => b.ReadUInt16()).ToList();
        var keyPosFormats = Enumerable.Range(0, (int) ECompressionFormat.eAutomaticQuat).Select(_ => b.ReadUInt32()).ToList();

        var keyRotLengths = Enumerable.Range(0, (int) NumKeyRot).Select(_ => b.ReadUInt16()).ToList();
        var keyRotFormats = Enumerable.Range(0, (int) ECompressionFormat.eAutomaticQuat).Select(_ => b.ReadUInt32()).ToList();

        var keyTimeOffsets = Enumerable.Range(0, (int) NumKeyTime).Select(_ => b.ReadUInt32()).ToList();
        var keyPosOffsets = Enumerable.Range(0, (int) NumKeyPos).Select(_ => b.ReadUInt32()).ToList();
        var keyRotOffsets = Enumerable.Range(0, (int) NumKeyRot).Select(_ => b.ReadUInt32()).ToList();
        var trackLength = b.ReadUInt32();
        
        Debug.Assert(keyTimeOffsets.All(x => (x & 3) == 0));
        Debug.Assert(keyPosOffsets.All(x => (x & 3) == 0));
        Debug.Assert(keyRotOffsets.All(x => (x & 3) == 0));
        Debug.Assert((trackLength & 3) == 0);
        
        keyRotOffsets.Add(trackLength);
        keyPosOffsets.Add(keyRotOffsets.First());
        keyTimeOffsets.Add(keyPosOffsets.First());

        var trackOffset = b.BaseStream.Position;
        if ((trackOffset & 3) != 0)
            trackOffset = (trackOffset & ~3) + 4; 

        KeyTimes = new List<List<float>>();
        foreach (var (length, from) in keyTimeLengths.Zip(keyTimeOffsets))
        {
            b.BaseStream.Position = trackOffset + from;

            List<float> data;
            if (keyTimeFormats[(int)EKeyTimesFormat.eF32] > 0)
            {
                --keyTimeFormats[(int)EKeyTimesFormat.eF32];
                data = Enumerable.Range(0, length).Select(_ => b.ReadSingle()).ToList();
            }
            else if (keyTimeFormats[(int)EKeyTimesFormat.eUINT16] > 0)
            {
                --keyTimeFormats[(int)EKeyTimesFormat.eUINT16];
                data = Enumerable.Range(0, length).Select(_ => (float)b.ReadUInt16()).ToList();
            }
            else if (keyTimeFormats[(int)EKeyTimesFormat.eByte] > 0)
            {
                --keyTimeFormats[(int)EKeyTimesFormat.eByte];
                data = b.ReadBytes(length).Select(x => (float) x).ToList();
            }
            else if (keyTimeFormats[(int)EKeyTimesFormat.eF32StartStop] > 0)
                throw new NotImplementedException();
            else if (keyTimeFormats[(int)EKeyTimesFormat.eUINT16StartStop] > 0)
                throw new NotImplementedException();
            else if (keyTimeFormats[(int)EKeyTimesFormat.eByteStartStop] > 0)
                throw new NotImplementedException();
            else if (keyTimeFormats[(int)EKeyTimesFormat.eBitset] > 0)
            {
                --keyTimeFormats[(int)EKeyTimesFormat.eBitset];
                var start = b.ReadUInt16();
                var end = b.ReadUInt16();
                var size = b.ReadUInt16();
                
                data = new List<float>(size);
                var keyValue = start;
                for (var i = 3; i < length; i++)
                {
                    var curr = b.ReadUInt16();
                    for (var j = 0; j < 16; ++j)
                    {
                        if (((curr >> j) & 1) != 0)
                            data.Add(keyValue);
                        ++keyValue;
                    }
                }
                
                if (data.Count != size)
                    Utilities.Log(LogLevelEnum.Warning, "eBitset: Expected {0} items, got {1} items", size, data.Count);
                if (data.Any() && Math.Abs(data[^1] - end) > float.Epsilon)
                    Utilities.Log(LogLevelEnum.Warning, "eBitset: Expected last as {0}, got {1}", end, data[^1]);
            }
            else
                throw new Exception("sum(count per format) != count of keytimes");
            
            // Get rid of decreasing entries at end (zero-pads)
            while (data.Count >= 2 && data[^2] > data[^1])
                data.RemoveAt(data.Count - 1);
            
            KeyTimes.Add(data);
        }

        KeyPositions = new List<List<Vector3>>();
        foreach (var (length, from) in keyPosLengths.Zip(keyPosOffsets))
        {
            b.BaseStream.Position = trackOffset + from;
            
            List<Vector3> data;
            if (keyPosFormats[(int) ECompressionFormat.eNoCompress] > 0)
                throw new NotImplementedException();
            else if (keyPosFormats[(int) ECompressionFormat.eNoCompressQuat] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eNoCompressQuat];
                data = Enumerable.Range(0, length).Select(_ => b.ReadQuaternion().DropW()).ToList();
            }
            else if (keyPosFormats[(int) ECompressionFormat.eNoCompressVec3] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eNoCompressVec3];
                data = Enumerable.Range(0, length).Select(_ => b.ReadVector3()).ToList();
            }
            else if (keyPosFormats[(int) ECompressionFormat.eShotInt3Quat] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eShotInt3Quat];
                data = Enumerable.Range(0, length).Select(_ => ((Quaternion)b.ReadShotInt3Quat()).DropW()).ToList();
            }
            else if (keyPosFormats[(int) ECompressionFormat.eSmallTreeDWORDQuat] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eSmallTreeDWORDQuat];
                data = Enumerable.Range(0, length).Select(_ => ((Quaternion)b.ReadSmallTreeDWORDQuat()).DropW()).ToList();
            }
            else if (keyPosFormats[(int) ECompressionFormat.eSmallTree48BitQuat] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eSmallTree48BitQuat];
                data = Enumerable.Range(0, length).Select(_ => ((Quaternion)b.ReadSmallTree48BitQuat()).DropW()).ToList();
            }
            else if (keyPosFormats[(int) ECompressionFormat.eSmallTree64BitQuat] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eSmallTree64BitQuat];
                data = Enumerable.Range(0, length).Select(_ => ((Quaternion)b.ReadSmallTree64BitQuat()).DropW()).ToList();
            }
            else if (keyPosFormats[(int) ECompressionFormat.ePolarQuat] > 0)
                throw new NotImplementedException();
            else if (keyPosFormats[(int) ECompressionFormat.eSmallTree64BitExtQuat] > 0)
            {
                --keyPosFormats[(int) ECompressionFormat.eSmallTree64BitExtQuat];
                data = Enumerable.Range(0, length).Select(_ => ((Quaternion)b.ReadSmallTree64BitExtQuat()).DropW()).ToList();
            }
            else
                throw new Exception("sum(count per format) != count of keypos");
            
            KeyPositions.Add(data);
        }

        KeyRotations = new List<List<Quaternion>>();
        foreach (var (length, from) in keyRotLengths.Zip(keyRotOffsets))
        {
            b.BaseStream.Position = trackOffset + from;
            
            List<Quaternion> data;
            if (keyRotFormats[(int) ECompressionFormat.eNoCompress] > 0)
                throw new NotImplementedException();
            else if (keyRotFormats[(int) ECompressionFormat.eNoCompressQuat] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eNoCompressQuat];
                data = Enumerable.Range(0, length).Select(_ => b.ReadQuaternion()).ToList();
            }
            else if (keyRotFormats[(int) ECompressionFormat.eNoCompressVec3] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eNoCompressVec3];
                data = Enumerable.Range(0, length).Select(_ => new Quaternion(b.ReadVector3(), float.NaN)).ToList();
            }
            else if (keyRotFormats[(int) ECompressionFormat.eShotInt3Quat] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eShotInt3Quat];
                data = Enumerable.Range(0, length).Select(_ => (Quaternion)b.ReadShotInt3Quat()).ToList();
            }
            else if (keyRotFormats[(int) ECompressionFormat.eSmallTreeDWORDQuat] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eSmallTreeDWORDQuat];
                data = Enumerable.Range(0, length).Select(_ => (Quaternion)b.ReadSmallTreeDWORDQuat()).ToList();
            }
            else if (keyRotFormats[(int) ECompressionFormat.eSmallTree48BitQuat] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eSmallTree48BitQuat];
                data = Enumerable.Range(0, length).Select(_ => (Quaternion)b.ReadSmallTree48BitQuat()).ToList();
            }
            else if (keyRotFormats[(int) ECompressionFormat.eSmallTree64BitQuat] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eSmallTree64BitQuat];
                data = Enumerable.Range(0, length).Select(_ => (Quaternion)b.ReadSmallTree64BitQuat()).ToList();
            }
            else if (keyRotFormats[(int) ECompressionFormat.ePolarQuat] > 0)
                throw new NotImplementedException();
            else if (keyRotFormats[(int) ECompressionFormat.eSmallTree64BitExtQuat] > 0)
            {
                --keyRotFormats[(int) ECompressionFormat.eSmallTree64BitExtQuat];
                data = Enumerable.Range(0, length).Select(_ => (Quaternion)b.ReadSmallTree64BitExtQuat()).ToList();
            }
            else
            {
                throw new Exception("sum(count per format) != count of keyRot");
            }
            
            KeyRotations.Add(data);
        }
        
        b.BaseStream.Position = trackOffset + trackLength;

        Animations = Enumerable.Range(0, (int) NumAnims).Select(_ => new Animation(b)).ToList();
    }
    
    public enum EKeyTimesFormat
    {
        eF32,
        eUINT16,
        eByte,
        eF32StartStop,  // unused
        eUINT16StartStop,  // unused
        eByteStartStop,  // unused
        eBitset,
    }

    public enum ECompressionFormat
    {
        eNoCompress = 0,
        eNoCompressQuat = 1,
        eNoCompressVec3 = 2,
        eShotInt3Quat = 3,
        eSmallTreeDWORDQuat = 4,
        eSmallTree48BitQuat = 5,
        eSmallTree64BitQuat = 6,
        ePolarQuat = 7,
        eSmallTree64BitExtQuat = 8,
        eAutomaticQuat = 9
    }

    [Flags]
    public enum AssetFlags : uint
    { 
        Additive = 0x001,
        Cycle = 0x002,
        Loaded = 0x004,
        Lmg = 0x008,
        LmgValid = 0x020,
        Created = 0x800,
        Requested =   0x1000,
        Ondemand = 0x2000,
        Aimpose = 0x4000,
        AimposeUnloaded = 0x8000,
        NotFound = 0x10000,
        Tcb = 0x20000,
        Internaltype = 0x40000,
        BigEndian = 0x80000000,
    }

    public struct MotionParams905
    {
        public AssetFlags AssetFlags;
        public uint Compression;

        public int TicksPerFrame;
        public float SecsPerTick;
        public int Start;
        public int End;

        public float MoveSpeed;
        public float TurnSpeed;
        public float AssetTurn;
        public float Distance;
        public float Slope;

        public Quaternion StartLocationQ;
        public Vector3 StartLocationV;
        public Quaternion EndLocationQ;
        public Vector3 EndLocationV;

        public float LHeelStart;
        public float LHeelEnd;
        public float LToe0Start;
        public float LToe0End;
        public float RHeelStart;
        public float RHeelEnd;
        public float RToe0Start;
        public float RToe0End;

        public MotionParams905()
        {
            AssetFlags = 0;
            Compression = 0xFFFFFFFFu;
            TicksPerFrame = 0;
            SecsPerTick = 0;
            Start = 0;
            End = 0;
            MoveSpeed = -1;
            TurnSpeed = -1;
            AssetTurn = -1;
            Distance = -1;
            Slope = -1;
            StartLocationQ = Quaternion.Identity;
            StartLocationV = Vector3.One;
            EndLocationQ = Quaternion.Identity;
            EndLocationV = Vector3.One;
            LHeelStart = -1;
            LHeelEnd = -1;
            LToe0Start = -1;
            LToe0End = -1;
            RHeelStart = -1;
            RHeelEnd = -1;
            RToe0Start = -1;
            RToe0End = -1;
        }

        public MotionParams905(BinaryReader r)
        {
            AssetFlags = (AssetFlags)r.ReadUInt32();
            Compression = r.ReadUInt32();
            TicksPerFrame = r.ReadInt32();
            SecsPerTick = r.ReadSingle();
            Start = r.ReadInt32();
            End = r.ReadInt32();
            MoveSpeed = r.ReadSingle();
            TurnSpeed = r.ReadSingle();
            AssetTurn = r.ReadSingle();
            Distance = r.ReadSingle();
            Slope = r.ReadSingle();
            StartLocationQ = r.ReadQuaternion();
            StartLocationV = r.ReadVector3();
            EndLocationQ = r.ReadQuaternion();
            EndLocationV = r.ReadVector3();
            LHeelStart = r.ReadSingle();
            LHeelEnd = r.ReadSingle();
            LToe0Start = r.ReadSingle();
            LToe0End = r.ReadSingle();
            RHeelStart = r.ReadSingle();
            RHeelEnd = r.ReadSingle();
            RToe0Start = r.ReadSingle();
            RToe0End = r.ReadSingle();
        }
    }

    public struct CControllerInfo
    {
        public const int InvalidTrack = -1;
        public int ControllerID;
        public int PosKeyTimeTrack;
        public int PosTrack;
        public int RotKeyTimeTrack;
        public int RotTrack;

        public bool HasPosTrack => PosTrack != InvalidTrack && PosKeyTimeTrack != InvalidTrack;
        public bool HasRotTrack => RotTrack != InvalidTrack && RotKeyTimeTrack != InvalidTrack;

        public CControllerInfo()
        {
            ControllerID = int.MaxValue;
            PosKeyTimeTrack = PosTrack = RotKeyTimeTrack = RotTrack = InvalidTrack;
        }

        public CControllerInfo(BinaryReader r)
        {
            ControllerID = r.ReadInt32();
            PosKeyTimeTrack = r.ReadInt32();
            PosTrack = r.ReadInt32();
            RotKeyTimeTrack = r.ReadInt32();
            RotTrack = r.ReadInt32();
        }
    }

    public struct Animation
    {
        public string Name;
        public MotionParams905 MotionParams;
        public byte[] FootPlanBits;
        public List<CControllerInfo> Controllers;

        public Animation(BinaryReader b)
        {
            var nameLen = b.ReadUInt16();
            Name = Encoding.UTF8.GetString(b.ReadBytes(nameLen));
            
            MotionParams = new MotionParams905(b);

            var footPlantBitsCount = b.ReadUInt16();
            FootPlanBits = b.ReadBytes(footPlantBitsCount);

            var controllerCount = b.ReadUInt16();

            Controllers = new List<CControllerInfo>();
            for (var j = 0u; j < controllerCount; j++)
                Controllers.Add(new CControllerInfo(b));
        }
    }
}

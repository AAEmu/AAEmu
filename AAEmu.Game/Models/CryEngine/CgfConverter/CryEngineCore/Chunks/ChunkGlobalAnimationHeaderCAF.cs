using System.Numerics;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkGlobalAnimationHeaderCAF : Chunk
{
    public uint Flags;

    public string FilePath;  // fixed 256 bytes
    public uint FilePathCRC32;
    public uint FilePathDBACRC32;

    public float LHeelStart;
    public float LHeelEnd;
    public float LToe0Start;
    public float LToe0End;
    public float RHeelStart;
    public float RHeelEnd;
    public float RToe0Start;
    public float RToe0End;

    public float StartSec;
    public float EndSec;
    public float TotalDuration;
    public uint Controllers;

    public Quaternion StartLocation;
    public Quaternion LastLocatorKey;
    public Vector3 Velocity;
    public float Distance;
    public float Speed;
    public float Slope;
    public float TurnSpeed;
    public float AssetTurn;
}
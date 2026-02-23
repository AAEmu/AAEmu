using CgfConverter.Services;
using System.IO;

namespace CgfConverter.CryEngineCore.Chunks;

internal sealed class ChunkController_829 : ChunkController
{
    public uint ControllerID { get; internal set; } 
    public ushort NumRotationKeys { get; internal set; }
    public ushort NumPositionKeys { get; internal set; }
    public byte RotationFormat { get; internal set; }
    public byte RotationTimeFormat { get; internal set; }
    public byte PositionFormat { get; internal set; }
    public byte PositionKeysInfo { get; internal set; }
    public byte PositionTimeFormat { get; internal set; }
    public byte TracksAligned { get; internal set; }

    public override void Read(BinaryReader b)
    {
        base.Read(b);

        ((EndiannessChangeableBinaryReader) b).IsBigEndian = false;

    }
}

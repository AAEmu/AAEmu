using Extensions;
using System.IO;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkController_826 : ChunkController
{
    public CtrlType ControllerType { get; internal set; }
    public int NumKeys { get; internal set; }
    public uint ControllerFlags { get; internal set; }        // technically a bitstruct to identify a cycle or a loop.
    public uint ControllerID { get; internal set; }           // Unique id based on CRC32 of bone name.  Ver 827 only?
    public Key[] Keys { get; internal set; }                    // array length NumKeys.  Ver 827?

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}, Number of Keys: {NumKeys}, Controller ID: {ControllerID:X}, Controller Type: {ControllerType}, Controller Flags: {ControllerFlags}";
    
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        //Utils.Log(LogLevelEnum.Debug, "ID is:  {0}", id);
        ControllerType = (CtrlType)b.ReadUInt32();
        NumKeys = b.ReadInt32();
        ControllerFlags = b.ReadUInt32();
        ControllerID = b.ReadUInt32();
        Keys = new Key[NumKeys];

        for (int i = 0; i < NumKeys; i++)
        {
            // Will implement fully later.  Not sure I understand the structure, or if it's necessary.
            Keys[i].Time = b.ReadInt32();
            Keys[i].AbsPos = b.ReadVector3();
            Keys[i].RelPos = b.ReadVector3();
        }
    }
}
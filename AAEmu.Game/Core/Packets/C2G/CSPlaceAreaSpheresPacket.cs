using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO: the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSPlaceAreaSpheresPacket() : GamePacket(CSOffsets.CSPlaceAreaSpheresPacket, 1)
{
    public uint Count { get; private set; }
    public int TypeValue { get; private set; }
    public uint Kind { get; private set; }
    public float PosX { get; private set; }
    public float PosY { get; private set; }
    public float PosZ { get; private set; }
    public float Radius { get; private set; }

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        TypeValue = stream.ReadInt32();
        Kind = stream.ReadUInt32();
        PosX = stream.ReadSingle();
        PosY = stream.ReadSingle();
        PosZ = stream.ReadSingle();
        Radius = stream.ReadSingle();
    }
}

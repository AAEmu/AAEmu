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
public class CSPlaceCommonFarmPacket() : GamePacket(CSOffsets.CSPlaceCommonFarmPacket, 1)
{
    public int TypeValue { get; private set; }
    public uint Count { get; private set; }
    public float PointX { get; private set; }
    public float PointY { get; private set; }
    public float PointZ { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Count = stream.ReadUInt32();
        PointX = stream.ReadSingle();
        PointY = stream.ReadSingle();
        PointZ = stream.ReadSingle();
    }
}

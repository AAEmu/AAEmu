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
public class CSAddResidentServicePointPacket() : GamePacket(CSOffsets.CSAddResidentServicePointPacket, 1)
{
    public short TypeValue { get; private set; }
    public ulong TypeValue2 { get; private set; }
    public uint Point { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt16();
        TypeValue2 = stream.ReadUInt64();
        Point = stream.ReadUInt32();
    }
}

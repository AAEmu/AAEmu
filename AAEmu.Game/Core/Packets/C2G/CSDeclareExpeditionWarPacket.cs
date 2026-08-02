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
public class CSDeclareExpeditionWarPacket() : GamePacket(CSOffsets.CSDeclareExpeditionWarPacket, 1)
{
    public uint Bc { get; private set; }
    public uint Money { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
        Money = stream.ReadUInt32();
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The board's reply to an order action the client asked for: what was attempted and whether it
/// worked. The client reads exactly these two bytes.
/// </summary>
public class SCCraftOrderActionResultPacket(byte kind, bool result) : GamePacket(SCOffsets.SCCraftOrderActionResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(kind);
        stream.Write(result);
        return stream;
    }
}

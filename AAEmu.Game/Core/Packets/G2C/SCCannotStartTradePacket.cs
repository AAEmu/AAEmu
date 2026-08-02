using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCCannotStartTradePacket(uint objId, int reason) : GamePacket(SCOffsets.SCCannotStartTradePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(reason);
        return stream;
    }
}

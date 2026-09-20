using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Recent listing fees for one craft: type, lowest, highest, then whether those two amounts
/// are real. The post dialog only copies lowest / highest when the flag is true.
/// </summary>
public class SCCraftOrderFeeInfoPacket(int @type, ulong moneyAmount, ulong moneyAmount2, bool result) : GamePacket(SCOffsets.SCCraftOrderFeeInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(moneyAmount);
        stream.Write(moneyAmount2);
        stream.Write(result);
        return stream;
    }
}

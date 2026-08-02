using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCBattleFieldBestRatingRewardPacket(int @type, uint oldRating, uint newRating, uint rewardAmount) : GamePacket(SCOffsets.SCBattleFieldBestRatingRewardPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(oldRating);
        stream.Write(newRating);
        stream.Write(rewardAmount);
        return stream;
    }
}

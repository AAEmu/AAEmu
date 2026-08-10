using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The leadership figure the peer-rating gate reads. Sent with SCHeroSeasonInfo, never instead of it.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer at .text 0xaa3da0, which
/// passes each value's name alongside the value: type i32, score i32.
///
/// The name suggests a packet for a season that is not running, but the handler (.text 0x108820) does
/// not care about any of that: it stores score into ClientPlayer[0x68] + 0xef0 and raises UI event
/// 0x2bf, ignoring type entirely. That field is the ONLY leadership the rating gate at 0x164f20 will
/// look at, so this goes out whenever leadership changes - see HeroManager.SendLeadership.
/// </remarks>
public class SCHeroSeasonOffPacket(int @type, int score) : GamePacket(SCOffsets.SCHeroSeasonOffPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(score);
        return stream;
    }
}

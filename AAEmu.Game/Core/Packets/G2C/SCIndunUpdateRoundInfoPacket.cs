using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The round's own time limit, so the client can draw the round timer — for zone group 130 that is the
/// "time until dawn" countdown of the night phase.
/// </summary>
/// <remarks>
/// Field order, widths and offsets come from the 10.0.2.13 client's handler: <c>curRound</c> u8@16,
/// <c>roundLimitTime</c> u32@20, <c>roundPlayTime</c> u32@24, <c>isTimeLimitRound</c> u8@28,
/// <c>bossRound</c> u8@29. The limit comes from the round's authored <c>indun_rounds.timer</c>, never a
/// per-instance constant.
/// </remarks>
public class SCIndunUpdateRoundInfoPacket(
    sbyte curRound,
    uint roundLimitTime,
    uint roundPlayTime,
    bool isTimeLimitRound,
    bool bossRound) : GamePacket(SCOffsets.SCIndunUpdateRoundInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(curRound);
        stream.Write(roundLimitTime);
        stream.Write(roundPlayTime);
        stream.Write(isTimeLimitRound);
        stream.Write(bossRound);
        return stream;
    }
}

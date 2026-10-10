using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The round's own time limit, so the client can draw the round timer — for zone group 130 that is the
/// "time until dawn" countdown of the night phase.
/// </summary>
/// <remarks>
/// Wire order (client serializer): <c>u8 curRound</c>, <c>bool isTimeLimitRound</c>,
/// <c>u32 roundLimitTime</c>, <c>u32 roundPlayTime</c>, <c>bool bossRound</c>. Struct field offsets in
/// the client object are not wire order. The limit comes from the round's authored
/// <c>indun_rounds.timer</c>, never a per-instance constant.
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
        stream.Write(isTimeLimitRound);
        stream.Write(roundLimitTime);
        stream.Write(roundPlayTime);
        stream.Write(bossRound);
        return stream;
    }
}

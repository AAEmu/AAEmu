using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrialInfoPacket(
    uint playerId,
    int crimePoint,
    int arrest,
    int acceptGuilty,
    int acceptTrial,
    int notGuilty,
    int guilty,
    int total,
    int cur,
    int crimeRecordCount,
    int botReport)
    : GamePacket(SCOffsets.SCTrialInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(playerId);
        stream.Write(crimePoint);
        stream.Write(arrest);
        stream.Write(acceptGuilty);
        stream.Write(acceptTrial);
        stream.Write(notGuilty);
        stream.Write(guilty);
        stream.Write(total);
        stream.Write(cur);
        stream.Write(crimeRecordCount);
        stream.Write(botReport);
        return stream;
    }

}

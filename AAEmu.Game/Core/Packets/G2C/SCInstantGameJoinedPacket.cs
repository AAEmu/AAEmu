using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameJoinedPacket(ZoneInstanceId zoneInstanceId, InstantCorps corps, GameRuleSet ruleSet)
    : GamePacket(SCOffsets.SCInstantGameJoinedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write((byte)corps);

        stream.Write(ruleSet.VictoryScore); // victoryScore
        stream.Write((ushort)0); // victoryKillCount
        stream.Write((uint)0); // victoryKillCorpsHead (npc template ID, it seems?)
        stream.Write(ruleSet.TimeOpening * 60 * 1000); // opening time
        stream.Write(ruleSet.TimePlaying * 60 * 1000); // playing time
        stream.Write(ruleSet.TimeEnding * 60 * 1000); // ending time
        stream.Write(ruleSet.Corps1FactionId); // corpsId[0]
        stream.Write(ruleSet.Corps2FactionId); // corpsId[1]

        for (var i = 0; i < 24; i++)
        {
            stream.Write((uint)0); // killstreak skill id
        }

        return stream;
    }
}
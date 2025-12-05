using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameJoinedPacket : GamePacket
{
    private ZoneInstanceId _zoneInstanceId;
    private InstantCorps _corps;
    private GameRuleSet _ruleSet;

    public SCInstantGameJoinedPacket(ZoneInstanceId zoneInstanceId, InstantCorps corps, GameRuleSet ruleSet)
        : base(SCOffsets.SCInstantGameJoinedPacket, 1)
    {
        _zoneInstanceId = zoneInstanceId;
        _corps = corps;
        _ruleSet = ruleSet;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneInstanceId);
        stream.Write((byte)_corps);

        stream.Write(_ruleSet.VictoryScore); // victoryScore
        stream.Write((ushort)0); // victoryKillCount
        stream.Write((uint)0); // victoryKillCorpsHead (npc template ID, it seems?)
        stream.Write(_ruleSet.TimeOpening * 60 * 1000); // opening time
        stream.Write(_ruleSet.TimePlaying * 60 * 1000); // playing time
        stream.Write(_ruleSet.TimeEnding * 60 * 1000); // ending time
        stream.Write(_ruleSet.Corps1FactionId); // corpsId[0]
        stream.Write(_ruleSet.Corps2FactionId); // corpsId[1]

        for (var i = 0; i < 24; i++)
        {
            stream.Write((uint)0); // killstreak skill id
        }

        return stream;
    }
}
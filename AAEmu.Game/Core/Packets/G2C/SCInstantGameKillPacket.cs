using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameKillPacket(
    ZoneInstanceId zoneInstanceId,
    Character killer,
    Character victim,
    InstantCorps killerCorps,
    InstantCorps victimCorps,
    sbyte killerKillstreak,
    int killerCorpsKills,
    int victimCorpsDeaths)
    : GamePacket(SCOffsets.SCInstantGameKillPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);

        stream.Write(killer.Id);
        stream.Write(victim.Id);

        stream.Write((byte)killerCorps);
        stream.Write((byte)victimCorps);

        stream.Write(killerKillstreak);

        stream.Write(killer.Name);
        stream.Write(victim.Name);

        stream.Write(killerCorpsKills);
        stream.Write(victimCorpsDeaths);

        return stream;
    }
}
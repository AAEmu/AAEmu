using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameKillPacket : GamePacket
{
    private readonly ZoneInstanceId _zoneInstanceId;

    private readonly Character _killer;
    private readonly Character _victim;

    private readonly InstantCorps _killerCorps;
    private readonly InstantCorps _victimCorps;

    private readonly sbyte _killerKillstreak;
    private readonly int _killerCorpsKills;
    private readonly int _victimCorpsDeaths;


    public SCInstantGameKillPacket(ZoneInstanceId zoneInstanceId, Character killer, Character victim, InstantCorps killerCorps, InstantCorps victimCorps, sbyte killerKillstreak, int killerCorpsKills, int victimCorpsDeaths)
        : base(SCOffsets.SCInstantGameKillPacket, 1)
    {
        _zoneInstanceId = zoneInstanceId;
        _killer = killer;
        _victim = victim;
        _killerCorps = killerCorps;
        _victimCorps = victimCorps;
        _killerKillstreak = killerKillstreak;
        _killerCorpsKills = killerCorpsKills;
        _victimCorpsDeaths = victimCorpsDeaths;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneInstanceId);

        stream.Write(_killer.Id);
        stream.Write(_victim.Id);

        stream.Write((byte)_killerCorps);
        stream.Write((byte)_victimCorps);

        stream.Write(_killerKillstreak);

        stream.Write(_killer.Name);
        stream.Write(_victim.Name);

        stream.Write(_killerCorpsKills);
        stream.Write(_victimCorpsDeaths);

        return stream;
    }
}
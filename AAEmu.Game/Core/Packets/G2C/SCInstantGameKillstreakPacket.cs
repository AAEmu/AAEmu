using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameKillstreakPacket : GamePacket
{
    private ZoneInstanceId _zoneInstanceId;
    private sbyte _killstreak;
    private uint _skillId;
    private bool _enabled;

    public SCInstantGameKillstreakPacket(ZoneInstanceId zoneInstanceId, sbyte killstreak, uint skillId, bool enabled)
        : base(SCOffsets.SCInstantGameKillstreakPacket, 1)
    {
        _zoneInstanceId = zoneInstanceId;
        _killstreak = killstreak;
        _skillId = skillId;
        _enabled = enabled;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneInstanceId);
        stream.Write(_killstreak);
        stream.Write(_skillId);
        stream.Write(_enabled);
        return stream;
    }
}
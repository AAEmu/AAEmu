using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameKillstreakPacket(ZoneInstanceId zoneInstanceId, sbyte killstreak, uint skillId, bool enabled)
    : GamePacket(SCOffsets.SCInstantGameKillstreakPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(killstreak);
        stream.Write(skillId);
        stream.Write(enabled);
        return stream;
    }
}
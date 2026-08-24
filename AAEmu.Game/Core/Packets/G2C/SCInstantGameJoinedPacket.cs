using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Entered match. Wire: zi (zone + world), u32 type (= instances.id / catalog).</summary>
public class SCInstantGameJoinedPacket(ZoneInstanceId zoneInstanceId, uint type)
    : GamePacket(SCOffsets.SCInstantGameJoinedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(type);
        return stream;
    }
}

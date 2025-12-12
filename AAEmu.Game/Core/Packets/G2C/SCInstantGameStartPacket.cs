using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameStartPacket(ZoneInstanceId zoneInstanceId, uint start, uint now)
    : GamePacket(SCOffsets.SCInstantGameStartPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(now);
        stream.Write(start);
        return stream;
    }
}
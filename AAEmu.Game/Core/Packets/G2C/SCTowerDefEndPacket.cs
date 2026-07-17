using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTowerDefEndPacket(TowerDefKey key, uint eventZoneId) : GamePacket(SCOffsets.SCTowerDefEndPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(key);
        stream.Write(eventZoneId);
        return stream;
    }
}

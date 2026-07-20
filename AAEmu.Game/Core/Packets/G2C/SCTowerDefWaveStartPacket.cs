using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTowerDefWaveStartPacket(TowerDefKey key, uint eventZoneId, uint step)
    : GamePacket(SCOffsets.SCTowerDefWaveStartPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(key);
        stream.Write(eventZoneId);
        stream.Write(step);
        return stream;
    }
}

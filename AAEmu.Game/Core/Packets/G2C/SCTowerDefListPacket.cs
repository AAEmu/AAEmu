using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTowerDefListPacket(List<TowerDefInfo> towerDefInfos) : GamePacket(SCOffsets.SCTowerDefListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(towerDefInfos.Count);
        foreach (var towerDefInfo in towerDefInfos)
            towerDefInfo.Write(stream);
        return stream;
    }
}

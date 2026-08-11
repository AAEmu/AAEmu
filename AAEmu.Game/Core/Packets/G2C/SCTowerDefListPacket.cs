using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.TowerDefs;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTowerDefListPacket(IReadOnlyList<TowerDefInfo> towerDefInfos) : GamePacket(SCOffsets.SCTowerDefListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var list = towerDefInfos ?? Array.Empty<TowerDefInfo>();
        // Client count field is u8 (max 100 entries).
        var count = list.Count > 100 ? 100 : list.Count;
        stream.Write((byte)count);
        for (var i = 0; i < count; i++)
            list[i].Write(stream);
        return stream;
    }
}

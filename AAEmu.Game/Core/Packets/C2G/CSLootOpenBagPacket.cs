using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootOpenBagPacket : GamePacket
{
    public CSLootOpenBagPacket() : base(CSOffsets.CSLootOpenBagPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var obj2Id = stream.ReadBc();
        var lootAll = stream.ReadBoolean();
        // TODO check the distance to the loot to be picked up
        var dist = stream.ReadSingle();
        var autoLoot = stream.ReadBoolean();

        ItemManager.Instance.TookLootDropItems(Connection.ActiveChar, objId, obj2Id, lootAll, dist, autoLoot);

    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootOpenBagPacket() : GamePacket(CSOffsets.CSLootOpenBagPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var obj2Id = stream.ReadBc();
        var lootAll = stream.ReadBoolean();

        var lootOwner = WorldManager.Instance.GetBaseUnit(objId);
        var object2 = WorldManager.Instance.GetBaseUnit(obj2Id);
        
        lootOwner?.LootingContainer.OpenBag(Connection.ActiveChar, object2, lootAll);
    }
}

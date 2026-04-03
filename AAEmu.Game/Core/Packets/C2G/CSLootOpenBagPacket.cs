using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;
using System.Linq;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootOpenBagPacket() : GamePacket(CSOffsets.CSLootOpenBagPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var obj2Id = stream.ReadBc();
        var lootAll = stream.ReadBoolean();

        var lootOwner = Connection.ActiveChar.ParentWorld.GetBaseUnit(objId);
        var object2 = Connection.ActiveChar.ParentWorld.GetBaseUnit(obj2Id);

        bool IsFuncDrivenLootDoodad(Doodad d) =>
            // Doodads with these funcs are looted by calling doodad.Use(...) instead of LootingContainer.OpenBag().
            d.CurrentFuncs.Any(func => Doodad.IsFuncDrivenLootFunc(func.FuncType));

        bool TryHandleFuncDrivenLoot(BaseUnit target)
        {
            if (target is not Doodad doodad)
                return false;
            if (doodad.LootingContainer.Items.Count > 0 || !IsFuncDrivenLootDoodad(doodad))
                return false;

            doodad.Use(Connection.ActiveChar, 0);
            // For one-shot loot doodads (ship debris), remove object when it transitions out of loot-capable funcs.
            if (!IsFuncDrivenLootDoodad(doodad))
                TaskManager.Instance.Schedule(new DoodadDeleteTask(doodad));
            return true;
        }

        // Some lootable doodads (e.g. ship debris) are implemented via DoodadFuncLootItem/LootPack
        // and do not use LootingContainer.Items; route loot-open to doodad.Use().
        if (TryHandleFuncDrivenLoot(lootOwner) || TryHandleFuncDrivenLoot(object2))
            return;

        lootOwner?.LootingContainer.OpenBag(Connection.ActiveChar, object2, lootAll);
    }
}

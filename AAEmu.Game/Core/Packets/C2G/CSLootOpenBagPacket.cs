using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Interactions;
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
            // Doodads handled by doodad.Use(...) instead of LootingContainer.OpenBag(). Must match Doodad.Write()'s
            // hasLootItem semantics exactly: ALL funcs of the current phase must be loot-driven. If a non-loot func
            // is present (CraftPack, StoreUi, Use, ...), the doodad is a multi-action object (e.g. workshop) and
            // must keep the interaction wheel, not the gear/loot icon.
            d.CurrentFuncs.Count > 0 && d.CurrentFuncs.All(func => Doodad.IsFuncDrivenLootFunc(func.FuncType));

        bool IsRecoverItemDoodad(Doodad d) =>
            // Trade packs / material packs are routed via DoodadFuncRecoverItem only for
            // the generic world pickup skill. Housing crate recover uses skill 15309 and
            // must NOT be consumed by CSLootOpenBagPacket, otherwise a right-click store
            // immediately triggers a recover and cancels the storage visually.
            d.CurrentFuncs.Any(func =>
                func.FuncType == "DoodadFuncRecoverItem" &&
                IsLootPacketRecoverSkill(func.SkillId));

        bool IsLootPacketRecoverSkill(uint skillId) =>
            // 11361 = generic RecoverItem pickup used by world trade/material packs.
            // 15309 = housing crate recover; keep it on the normal doodad interaction path.
            skillId == 11361;

        bool TryHandleFuncDrivenLoot(BaseUnit target)
        {
            if (target is not Doodad doodad)
                return false;
            if (doodad.LootingContainer.Items.Count > 0 || !IsFuncDrivenLootDoodad(doodad))
                return false;

            // Pack-style pickup -> route through RecoverItem with backpack guard, same as right-click (skill 11361).
            // This is the only safe path for DoodadFuncRecoverItem: it refuses to fire if the player already
            // wears a pack, preventing the duplication / pack-swap parasites observed on housing tradepacks.
            if (IsRecoverItemDoodad(doodad))
            {
                new RecoverItem().Execute(Connection.ActiveChar, null, doodad, null, 0, 0, null);
                return true;
            }

            // Other loot-driven doodads (e.g. ship debris with DoodadFuncLootItem/LootPack/Cutdowning):
            // legacy path is fine.
            doodad.Use(Connection.ActiveChar, 0);
            // For one-shot loot doodads, remove object when it transitions out of loot-capable funcs.
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

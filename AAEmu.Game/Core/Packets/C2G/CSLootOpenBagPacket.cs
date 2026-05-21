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
    // Generic RecoverItem pickup skill used by world trade/material packs (same path as the right-click pickup).
    // 15309 = housing crate recover; that one is intentionally NOT handled here and stays on the normal doodad
    // interaction path (see IsLootPacketRecoverSkill).
    private const uint GenericRecoverItemSkillId = 11361u;

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
            // the generic world pickup skill (GenericRecoverItemSkillId). Housing crate recover uses skill 15309 and
            // must NOT be consumed by CSLootOpenBagPacket, otherwise a right-click store
            // immediately triggers a recover and cancels the storage visually.
            d.CurrentFuncs.Any(func =>
                func.FuncType == "DoodadFuncRecoverItem" &&
                IsLootPacketRecoverSkill(func.SkillId));

        bool IsLootPacketRecoverSkill(uint skillId) =>
            // GenericRecoverItemSkillId = generic RecoverItem pickup used by world trade/material packs.
            // 15309 = housing crate recover; keep it on the normal doodad interaction path.
            skillId == GenericRecoverItemSkillId;

        bool TryHandleFuncDrivenLoot(BaseUnit target)
        {
            if (target is not Doodad doodad)
                return false;
            if (doodad.LootingContainer.Items.Count > 0 || !IsFuncDrivenLootDoodad(doodad))
                return false;

            // Pack-style pickup -> route through RecoverItem with backpack guard, same as right-click
            // (GenericRecoverItemSkillId). This is the only safe path for DoodadFuncRecoverItem: it refuses to fire
            // if the player already wears a pack, preventing the duplication / pack-swap parasites observed on
            // housing tradepacks. We forward the real recover skillId so the F-key path stays consistent with the
            // right-click path (DoodadFuncRecoverItem currently only logs it, but later checks/effects may rely on it).
            if (IsRecoverItemDoodad(doodad))
            {
                new RecoverItem().Execute(Connection.ActiveChar, null, doodad, null, GenericRecoverItemSkillId, 0, null);
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

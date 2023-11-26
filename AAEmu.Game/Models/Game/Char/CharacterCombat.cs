using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character
{
    public uint ResurrectHpPercent { get; set; } = 1;
    public uint ResurrectMpPercent { get; set; } = 1;
    public uint HostileFactionKills { get; set; }
    public uint HonorGainedInCombat { get; set; }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        base.DoDie(killer, killReason);

        if (killer is Character enemy && enemy.Faction.MotherId != Faction.MotherId)
            enemy.HostileFactionKills++;

        DropTradePackToFloor();
    }

    /// <summary>
    /// Force drop player's trade-pack (if any) to the floor
    /// </summary>
    private void DropTradePackToFloor()
    {
        // check trade packs to drop
        var item = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if ((item?.Template is BackpackTemplate backpackTemplate) &&
            (backpackTemplate.BackpackType == BackpackType.TradePack))
        {
            // Find the linked doodad of this item's put down effect
            var backpackDoodadId = 0u;
            var itemSkill = SkillManager.Instance.GetSkillTemplate(backpackTemplate.UseSkillId);
            foreach (var skillEffect in itemSkill.Effects)
            {
                if (skillEffect.Template is PutDownBackpackEffect putDownEffect)
                {
                    backpackDoodadId = putDownEffect.BackpackDoodadId;
                    break;
                }
            }

            if (backpackDoodadId > 0 && Inventory.SystemContainer.AddOrMoveExistingItem(Items.Actions.ItemTaskType.DropBackpack, item))
            {
                // Spawn doodad
                Logger.Trace("Spawn tradepack on floor on death");

                var doodad = DoodadManager.Instance.Create(0, backpackDoodadId, this, true);
                if (doodad == null)
                {
                    Logger.Warn($"Doodad {backpackDoodadId}, from BackpackDoodadId could not be created");
                    return;
                }

                doodad.IsPersistent = true;
                doodad.Transform = Transform.CloneDetached(doodad);
                doodad.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(doodad.Transform));
                doodad.AttachPoint = AttachPointKind.None;
                doodad.ItemId = item.Id;
                doodad.ItemTemplateId = item.Template.Id;
                doodad.UccId = item.UccId;
                doodad.SetScale(1f);
                doodad.PlantTime = DateTime.UtcNow;
                doodad.InitDoodad();
                doodad.Spawn();
                doodad.Save();

                BroadcastPacket(new SCUnitEquipmentsChangedPacket(ObjId, (byte)EquipmentItemSlot.Backpack, null), false);
            }

        }
    }
}

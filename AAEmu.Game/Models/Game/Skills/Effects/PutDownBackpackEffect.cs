using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class PutDownBackpackEffect : EffectTemplate
{
    public uint BackpackDoodadId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("PutDownBackpackEffect");

        var character = (Character)caster;
        if (character == null) return;

        var packItem = (SkillItem)casterObj;
        if (packItem == null) return;

        var item = character.Inventory.Equipment.GetItemByItemId(packItem.ItemId);
        if (item == null) return;

        if (PublicFarmManager.Instance.InPublicFarm(character.Transform.WorldId, character.Transform.World.Position))
        {
            character.SendErrorMessage(ErrorMessageType.CommonFarmNotAllowedType);
            return;
        }

        var previousGlider = character.Inventory.Bag.GetItemByItemId(character.Inventory.PreviousBackPackItemId);
        // If no longer valid, reset the value here
        if ((previousGlider == null) || (previousGlider.SlotType != SlotType.Bag))
            character.Inventory.PreviousBackPackItemId = 0;

        using var pos = character.Transform.CloneDetached();
        pos.Local.AddDistanceToFront(1f);
        //pos.Local.AddDistance(0f,1f,0f); // This function isn't finished yet
        pos.Local.SetRotation(0f, 0f, 0f); // Always faces north when placed

        var targetHouse = HousingManager.Instance.GetHouseAtLocation(pos.World.Position.X, pos.World.Position.Y);
        if (targetHouse != null)
        {
            // Trying to put on a house location, we need to do some checks
            if (!targetHouse.AllowedToInteract(character))
            {
                character.SendErrorMessage(ErrorMessageType.Backpack);
                return;
            }
        }

        if (character.Inventory.SystemContainer.AddOrMoveExistingItem(Items.Actions.ItemTaskType.DropBackpack, item))
        {
            // Spawn doodad
            Logger.Debug("PutDownPackEffect");

            var doodad = DoodadManager.Instance.Create(0, BackpackDoodadId, character, true);
            if (doodad == null)
            {
                Logger.Warn("Doodad {0}, from BackpackDoodadId could not be created", BackpackDoodadId);
                return;
            }
            doodad.IsPersistent = true;
            doodad.Transform = pos.CloneDetached(doodad);
            doodad.AttachPoint = AttachPointKind.None;
            doodad.ItemId = item.Id;
            doodad.ItemTemplateId = item.Template.Id;
            doodad.UccId = item.UccId; // Not sure if it's needed, but let's copy the Ucc for completeness' sake
            doodad.SetScale(1f);
            doodad.PlantTime = DateTime.UtcNow;
            if (targetHouse != null)
            {
                doodad.OwnerDbId = targetHouse.Id;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.ParentObj = targetHouse;
                doodad.ParentObjId = targetHouse.ObjId;
                doodad.Transform.Parent = targetHouse.Transform; // Does not work as intended yet
            }

            doodad.InitDoodad();
            doodad.Spawn();
            doodad.Save();

            character.BroadcastPacket(new SCUnitEquipmentsChangedPacket(character.ObjId, (byte)EquipmentItemSlot.Backpack, null), false);
            if ((previousGlider != null) && character.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack) == null)
                character.Inventory.SplitOrMoveItem(Items.Actions.ItemTaskType.SwapItems, previousGlider.Id,
                    previousGlider.SlotType, (byte)previousGlider.Slot, 0, SlotType.Equipment,
                    (int)EquipmentItemSlot.Backpack);
        }
    }
}

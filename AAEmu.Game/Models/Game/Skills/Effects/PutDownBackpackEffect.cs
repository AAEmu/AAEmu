using System;
using System.Numerics;
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
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class PutDownBackpackEffect : EffectTemplate
    {
        public uint BackpackDoodadId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("PutDownBackpackEffect");

            Character character = (Character)caster;
            if (character == null) return;

            SkillItem packItem = (SkillItem)casterObj;
            if (packItem == null) return;

            Item item = character.Inventory.Equipment.GetItemByItemId(packItem.ItemId);
            if (item == null) return;
            
            var pos = character.Transform.CloneDetached();
            pos.Local.AddDistanceToFront(1f);
            //pos.Local.AddDistance(0f,1f,0f); // This function isn't finished yet
            pos.Local.SetRotation(0f,0f,0f); // Always faces north when placed
            
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
            
            if (character.Inventory.SystemContainer.AddOrMoveExistingItem(Items.Actions.ItemTaskType.DropBackpack, item, (int)EquipmentItemSlot.Backpack))
            {
                // Spawn doodad
                _log.Debug("PutDownPackEffect");

                var doodad = DoodadManager.Instance.Create(0, BackpackDoodadId, character);
                if (doodad == null)
                {
                    _log.Warn("Doodad {0}, from BackpackDoodadId could not be created", BackpackDoodadId);
                    return ;
                }
                doodad.IsPersistent = true;
                doodad.Transform = pos.CloneDetached(doodad);
                doodad.AttachPoint = AttachPointKind.None ;
                doodad.ItemId = item.Id ;
                doodad.ItemTemplateId = item.Template.Id;
                doodad.SetScale(1f);
                doodad.PlantTime = DateTime.Now;
                if (targetHouse != null)
                {
                    doodad.DbHouseId = targetHouse.Id;
                    doodad.OwnerType = DoodadOwnerType.Housing;
                    doodad.ParentObj = targetHouse;
                    doodad.ParentObjId = targetHouse.ObjId;
                    doodad.Transform.Parent = targetHouse.Transform; // Does not work as intended yet
                }
                
                doodad.Spawn();
                doodad.Save();
                
                character.BroadcastPacket(new SCUnitEquipmentsChangedPacket(character.ObjId,(byte)EquipmentItemSlot.Backpack, null), false);
            }
        }
    }
}

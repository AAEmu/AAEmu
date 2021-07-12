using System;
using System.Numerics;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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

            if (character.Inventory.SystemContainer.AddOrMoveExistingItem(Items.Actions.ItemTaskType.DropBackpack, item, (int)EquipmentItemSlot.Backpack))
            {
                // Spawn doodad
                _log.Debug("PutDownPackEffect");

                var pos = character.Transform.CloneDetached();
                pos.Local.AddDistanceToFront(1f);

                var doodad = DoodadManager.Instance.Create(0, BackpackDoodadId);
                if (doodad == null)
                {
                    _log.Warn("Doodad {0}, from BackpackDoodadId could not be created", BackpackDoodadId);
                    return ;
                }
                doodad.OwnerId = character.Id;
                doodad.OwnerObjId = character.ObjId;
                doodad.Transform = pos.Clone(doodad);
                doodad.AttachPoint = 0 ;
                doodad.ItemId = item.Template.MaxCount > 1 ? item.Id : 0;
                doodad.SetScale(1f);
                doodad.Data = 0;
                doodad.PlantTime = DateTime.Now;
                //doodad.IsPersistent = false;
                doodad.Spawn();
            }
        }
    }
}

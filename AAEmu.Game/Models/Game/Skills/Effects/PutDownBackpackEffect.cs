using System;
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

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time)
        {
            _log.Debug("PutDownBackpackEffect");

            Character character = (Character)caster;
            if (character == null) return;

            SkillItem packItem = (SkillItem) casterObj;
            if (packItem == null) return;

            Item item = character.Inventory.Equipment.GetItemByItemId(packItem.ItemId);
            if (item == null) return;

            if (character.Inventory.Equipment.RemoveItem(Items.Actions.ItemTaskType.DropBackpack, item, true))
            {
                // Spawn doodad
                var doodadSpawner = new DoodadSpawner();
                doodadSpawner.Id = 0;
                doodadSpawner.UnitId = BackpackDoodadId;
                doodadSpawner.Position = character.Position.Clone();
                doodadSpawner.Spawn(0);
            }
        }
    }
}

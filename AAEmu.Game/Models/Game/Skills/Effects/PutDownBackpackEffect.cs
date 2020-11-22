using System;
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

            if (character.Inventory.Equipment.RemoveItem(Items.Actions.ItemTaskType.DropBackpack, item, true))
            {
                //TODO: save to database and who placed it down and item template shouldn't be used.

                // Spawn doodad
                _log.Debug("[PutDownPackEffect");
                var (newX, newY) = MathUtil.AddDistanceToFront(1, character.Position.X, character.Position.Y, character.Position.RotationZ);
                var pos = character.Position.Clone();

                pos.X = newX;
                pos.Y = newY;
                pos.RotationZ = 0; // packs always place facing north

                var doodadSpawner = new DoodadSpawner();
                doodadSpawner.Id = 0;
                doodadSpawner.UnitId = BackpackDoodadId;
                doodadSpawner.Position = pos;
                var doodad = doodadSpawner.Spawn(0);
                doodad.ItemId = item.TemplateId;
            }
        }
    }
}

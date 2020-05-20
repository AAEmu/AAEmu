using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ItemCapScale : SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int value1,
            int value2,
            int value3,
            int value4)
        {
            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

            var owner = (Character)caster;
            var temperSkillItem = (SkillItem)casterObj;
            var skillTargetItem = (SkillCastItemTarget)targetObj;

            if (owner == null)
            {
                return;
            }

            if (temperSkillItem == null)
            {
                return;
            }

            if (skillTargetItem == null)
            {
                return;
            }

            var targetItem = owner.Inventory.GetItemById(skillTargetItem.Id);
            var temperItem = owner.Inventory.GetItemById(temperSkillItem.ItemId);

            if (targetItem == null || temperItem == null)
            {
                return;
            }

            var equipItem = (EquipItem)targetItem;

            var itemCapScale = ItemManager.Instance.GetItemCapScale(skill.Id);

            var physicalScale = (ushort)Rand.Next(itemCapScale.ScaleMin, itemCapScale.ScaleMax);
            var magicalScale = (ushort)Rand.Next(itemCapScale.ScaleMin, itemCapScale.ScaleMax);

            equipItem.TemperPhysical = physicalScale;
            equipItem.TemperMagical = magicalScale;

            temperItem._holdingContainer.ConsumeItem(ItemTaskType.EnchantPhysical, temperItem.TemplateId, 1, temperItem);
            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EnchantPhysical, new List<ItemTask>() { new ItemUpdate(equipItem) }, new List<ulong>()));
        }
    }
}

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

        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3,
            int Value4)
        {
            var owner = (Character) caster;
            var temperSkillItem = (SkillItem) casterObj;
            var skillTargetItem = (SkillCastItemTarget) targetObj;

            if (owner == null) return;
            if (temperSkillItem == null) return;
            if (skillTargetItem == null) return;

            var targetItem = owner.Inventory.GetItem(skillTargetItem.Id);
            var temperItem = owner.Inventory.GetItem(temperSkillItem.ItemId);
            
            if (targetItem == null || temperItem == null) return;

            var equipItem = (EquipItem) targetItem;
            if (equipItem == null) return; 

            var tasksTempering = new List<ItemTask>();
            var tasksRemove = new List<ItemTask>();
            
            var itemCapScale = ItemManager.Instance.GetItemCapScale(skill.Id);

            var physicalScale = (ushort)Rand.Next(itemCapScale.ScaleMin, itemCapScale.ScaleMax);
            var magicalScale = (ushort)Rand.Next(itemCapScale.ScaleMin, itemCapScale.ScaleMax);

            equipItem.TemperPhysical = physicalScale;
            equipItem.TemperMagical = magicalScale;

            tasksTempering.Add(new ItemUpdate(equipItem));
            tasksRemove.Add(InventoryHelper.GetTaskAndRemoveItem(owner, temperItem, 1));

            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.EnchantPhysical, tasksTempering, new List<ulong>()));
            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, tasksRemove, new List<ulong>()));
        }
    }
}

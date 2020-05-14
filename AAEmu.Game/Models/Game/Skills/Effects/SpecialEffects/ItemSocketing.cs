using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ItemSocketing : SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3,
            int Value4)
        {
            var owner = (Character) caster;
            var gemSkillItem = (SkillItem) casterObj;
            var skillTargetItem = (SkillCastItemTarget) targetObj;

            if (owner == null) return;
            if (gemSkillItem == null) return;
            if (skillTargetItem == null) return;

            var targetItem = owner.Inventory.GetItem(skillTargetItem.Id);
            var gemItem = owner.Inventory.GetItem(gemSkillItem.ItemId);
            
            if (targetItem == null || gemItem == null) return;

            var equipItem = (EquipItem) targetItem;
            if (equipItem == null) return; 

            var tasksSocketing = new List<ItemTask>();
            
            var gemCount = 0u;
            foreach (var gem in equipItem.GemIds) {
                if (gem != 0) gemCount++;
            }
            
            // Check that we can put that gem in             

            // Add gem to proper slot
            var gemRoll = Rand.Next(0, 10000);
            var gemChance = ItemManager.Instance.GetSocketChance(gemCount);
            
            byte result = 0;
            if (gemRoll < gemChance) {
                equipItem.GemIds[gemCount] = gemItem.TemplateId;
                result = 1;
            } else {
                for (var i = 0; i < equipItem.GemIds.Length; i++)
                    equipItem.GemIds[i] = 0;
            }

            tasksSocketing.Add(new ItemUpdate(equipItem));

            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Socketing, tasksSocketing, new List<ulong>()));
            owner.SendPacket(new SCItemSocketingLunagemResultPacket(result, equipItem.Id, gemItem.TemplateId, true));
        }
    }
}

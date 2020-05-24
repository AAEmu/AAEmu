using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ReceiveLuluLeaflet : SpecialEffect
    {

        public void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3,
            int Value4)
        {
            var owner = (Character)caster;
            var skillData = (SkillItem)casterObj;

            var itemInfo = owner.Inventory.GetItemById(skillData.ItemId);
            if (itemInfo == null || itemInfo.Count <= 0) return;

            // TODO: use the selected item instead of the item template
            if (owner.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents,skillData.ItemTemplateId,1,itemInfo) <= 0)
            {
                // TODO: LOYALT IS ACCOUNT WIDE
                owner.SendPacket(new SCBmPointPacket(owner.BmPoint));
            }
            /*
            var tasks = new List<ItemTask>
            {
                InventoryHelper.GetTaskAndRemoveItem(owner, itemInfo, 1)
            };
            owner.BmPoint += Value1;
            owner.SendPacket(new SCBmPointPacket(owner.BmPoint));
            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, tasks, new List<ulong>()));
            */            
        }
    }
}

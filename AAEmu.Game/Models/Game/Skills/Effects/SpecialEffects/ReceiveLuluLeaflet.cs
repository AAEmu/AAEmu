using System;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ReceiveLuluLeaflet : SpecialEffect
    {

        public void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int value1, int value2, int value3,
            int value4)
        {
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: ReceiveLuluLeaflet value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            var owner = (Character)caster;
            var skillData = (SkillItem)casterObj;

            var itemInfo = owner.Inventory.GetItemById(skillData.ItemId);
            if (itemInfo == null || itemInfo.Count <= 0) return;

            if (owner.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, skillData.ItemTemplateId, 1, itemInfo) <= 0)
            {
                // TODO: LOYALTY IS ACCOUNT WIDE
                owner.BmPoint += value1;
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

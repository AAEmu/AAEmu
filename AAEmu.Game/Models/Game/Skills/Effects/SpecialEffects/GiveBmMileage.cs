using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class GiveBmMileage : SpecialEffectAction
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

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
            var skillData = (SkillItem)casterObj;

            var itemInfo = owner.Inventory.GetItem(skillData.ItemId);
            if (itemInfo == null || itemInfo.Count <= 0)
            {
                return;
            }

            var tasks = new List<ItemTask>
            {
                InventoryHelper.GetTaskAndRemoveItem(owner, itemInfo, 1)
            };
            owner.BmPoint += value1;
            owner.SendPacket(new SCBmPointPacket(owner.BmPoint));
            owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, tasks, new List<ulong>()));

            // TODO - LOYALT IS ACCOUNT WIDE
        }
    }
}

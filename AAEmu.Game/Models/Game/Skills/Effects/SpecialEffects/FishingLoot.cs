using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    class FishingLoot : SpecialEffectAction
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
            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", target.Position.ZoneId, value1, target.Name, targetObj.ObjId);


            //TODO: Need a way to determine if saltwater or freshwater fishing. I currently only use the seawater table for loot. 
            var zoneId = ZoneManager.Instance.GetZoneByKey(target.Position.ZoneId).GroupId;
            var lootTableId = ZoneManager.Instance.GetZoneGroupById(zoneId).FishingSeaLootPackId;

            //TODO: Should likely make a lootmanager to handle these cases. 
            var lootPacks = ItemManager.Instance.GetLootPacks(lootTableId);

            var totalDropRate = (int)lootPacks.Sum(c => c.DropRate); //Adds the total drop rate of all possible items from a skill

            Random rand = new Random();
            var randChoice = rand.Next(totalDropRate);

            LootPacks lootpack = null;

            foreach (var item in lootPacks) //Picks item based on a weighted system. The higher the droprate the more likely you are to get that item. 
            {
                if (randChoice < item.DropRate)
                {
                    lootpack = item;
                    break;
                }
                else
                    randChoice -= (int)item.DropRate;
            }

            var player = (Character)caster;

            var newItem = ItemManager.Instance.Create(lootpack.ItemId, 1, lootpack.GradeId, true);
            player.Inventory.Bag.AcquireDefaultItem(Items.Actions.ItemTaskType.Fishing, newItem.TemplateId,1,newItem.Grade);
        }
    }
}

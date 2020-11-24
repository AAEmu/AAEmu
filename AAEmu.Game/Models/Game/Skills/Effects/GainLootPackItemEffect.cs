using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class GainLootPackItemEffect : EffectTemplate
    {
        public uint LootPackId { get; set; }
        public bool ConsumeSourceItem { get; set; }
        public uint ConsumeItemId { get; set; }
        public int ConsumeCount { get; set; }
        public bool InheritGrade { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            var character = (Character)caster;
            if (character == null) return;

            var lootPack = (SkillItem)casterObj;
            if (lootPack == null) return;

            var lootPacks = ItemManager.Instance.GetLootPacks(LootPackId);
            var lootGroups = ItemManager.Instance.GetLootGroups(LootPackId);
            var lootPackItem = character.Inventory.GetItemById(lootPack.ItemId);

            _log.Debug("LootGroups {0}", lootGroups);

            List<LootPacks> lootItems = new List<LootPacks>();

            LootGroups lootGroup = null; // Need some form of loot manager for this. 
            if (lootGroups.Length > 0)
            {
                var totalDropRate = (int)lootGroups.Sum(c => c.DropRate);
                Random random = new Random();
                var randomNumber = random.Next(totalDropRate);

                foreach (var group in lootGroups)
                {
                    if (randomNumber < group.DropRate)
                    {
                        lootGroup = group;
                        break;
                    }
                    else
                        randomNumber -= (int)group.DropRate;
                }
            }
            else // Do this if there are no loot groups to use. 
            {
                var sortedLootPack = lootPacks.Where(c => c.AlwaysDrop != true).ToList();
                var totalDropRate = (int)sortedLootPack.Sum(c => c.DropRate);
                Random random = new Random();
                var randomNumber = random.Next(totalDropRate);

                foreach (var item in sortedLootPack)
                {
                    if (randomNumber < item.DropRate)
                    {
                        lootItems.Add(item);
                        break;
                    }
                    else
                        randomNumber -= (int)item.DropRate;
                }
            }

            if (lootGroup != null) // We picked a lootgroup from a list of groups. Now pick the items
            {
                var sortedLootPack = lootPacks.Where(c => c.Group == lootGroup.GroupNo && c.AlwaysDrop != true).ToList();
                var totalDropRate = (int)sortedLootPack.Sum(c => c.DropRate);
                Random random = new Random();
                var randomNumber = random.Next(totalDropRate);

                foreach (var item in sortedLootPack)
                {
                    if (randomNumber < item.DropRate)
                    {
                        lootItems.Add(item);
                        break;
                    }
                    else
                    {
                        randomNumber -= (int)item.DropRate;
                    }
                }
            }

            var alwaysDropItems = lootPacks.Where(c => c.AlwaysDrop == true).ToList();// Adds always dropped items to the list. 
            if (alwaysDropItems.Count > 0)
                lootItems.AddRange(alwaysDropItems);

            if (lootItems.Count > 0) // Add picked items to inventory
            {
                foreach (var item in lootItems)
                {
                    if (item.ItemId == 500) // handle gold
                    {
                        AddGold(character, item.MinAmount, item.MaxAmount);
                        continue;
                    }

                    Random rand = new Random();
                    var amount = rand.Next(item.MinAmount, item.MaxAmount + 1);

                    var grade = 0;
                    if (InheritGrade)
                        grade = GetGradeDistributionId(item.GradeId);

                    character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, item.ItemId, amount, grade);
                }
            }
            _log.Debug("GainLootPackItemEffect {0}", LootPackId);
        }

        private void AddGold(Unit caster, int goldMin, int goldMax)
        {
            var character = (Character)caster;
            if (character == null) return;
            var goldAdd = Rand.Next(goldMin, goldMax);
            var jackpot = Rand.Next(0, 10000); //TODO: Make this a config. 
            if (jackpot <= 50)
                goldAdd = goldAdd * 1000;

            if (jackpot <= 5)
                goldAdd = goldAdd * 5000;

            character.Money += goldAdd;
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillEffectGainItem,
                new List<ItemTask> { new MoneyChange(goldAdd) }, new List<ulong>()));
        }

        private byte GetGradeDistributionId(byte gradeId)
        {
            var gradeDist = ItemManager.Instance.GetGradeDistributions(gradeId);
            var array = new[]
            {
                gradeDist.Weight0,
                gradeDist.Weight1,
                gradeDist.Weight2,
                gradeDist.Weight3,
                gradeDist.Weight4,
                gradeDist.Weight5,
                gradeDist.Weight6,
                gradeDist.Weight7,
                gradeDist.Weight8,
                gradeDist.Weight9,
                gradeDist.Weight10,
                gradeDist.Weight11
            };
            var old = 0;
            var gradeDrop = Rand.Next(0, 100);
            for (byte i = 0; i <= 11; i++)
            {
                if (gradeDrop <= array[i] + old)
                {
                    gradeId = i;
                    i = 11;
                }
                else
                {
                    old += array[i];
                }
            }

            return gradeId;
        }
    }
}

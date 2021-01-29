using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

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

            var rowG = lootGroups.Length;
            var rowP = lootPacks.Length;

            var madeBy = 0u;
            if (lootPackItem != null)
                madeBy = lootPackItem.MadeUnitId;

            if (rowG >= 1)
            {
                // Do a check if player has enough space (group count)
                if (character.Inventory.Bag.FreeSlotCount < rowG)
                {
                    character.SendErrorMessage(Error.ErrorMessageType.BagFull);
                    return;
                }

                const float maxDropRate = (float)10000000;
                for (var i = 0; i < rowG; i++)
                {
                    var itemIdLoot = (uint)0;
                    var minAmount = 0;
                    var maxAmount = 0;
                    var gradeId = (byte)0;
                    var dropRateMax = (uint)0;
                    var dropRate = Rand.Next(0, maxDropRate);
                    var dropRateGroup = (uint)10000000;
                    if (lootGroups[i].GroupNo > 1 && rowG >= 2)
                    {
                        dropRateGroup = 0;
                        for (var di = 0; di < lootGroups[i].GroupNo; di++)
                            if (lootGroups[di].GroupNo > 1)
                                dropRateGroup += lootGroups[di].DropRate;
                    }

                    if (dropRateGroup >= dropRate)
                    {
                        for (var ui = 0; ui < rowP; ui++)
                        {
                            if (lootPacks[ui].Group == lootGroups[i].GroupNo)
                            {
                                dropRateMax += lootPacks[ui].DropRate;
                            }
                        }

                        var dropRateItem = Rand.Next(0, dropRateMax);
                        var dropRateItemId = (uint)0;
                        for (var uii = 0; uii < rowP; uii++)
                        {
                            if (lootPacks[uii].Group == lootGroups[i].GroupNo)
                            {
                                if (lootPacks[uii].DropRate + dropRateItemId >= dropRateItem)
                                {
                                    itemIdLoot = lootPacks[uii].ItemId;
                                    minAmount = lootPacks[uii].MinAmount;
                                    maxAmount = lootPacks[uii].MaxAmount;
                                    gradeId = lootPacks[uii].GradeId;
                                    uii = rowP;
                                }
                                else
                                {
                                    dropRateItemId += lootPacks[uii].DropRate;
                                }
                            }
                        }
                    }

                    if (minAmount > 1 && itemIdLoot == 500)
                    {
                        AddGold(caster, minAmount, maxAmount);
                    }

                    if (itemIdLoot > 0 && itemIdLoot != 500)
                    {
                        if (InheritGrade)
                            gradeId = lootPackItem.Grade;

                        if (lootGroups[i].ItemGradeDistributionId > 0)
                            gradeId = GetGradeDistributionId(lootGroups[i].ItemGradeDistributionId);

                        AddItem(caster, itemIdLoot, gradeId, minAmount, maxAmount, madeBy);
                    }
                }
            }
            else
            {
                if (rowP >= 1)
                {
                    // Do a check if player has enough space (pack count)
                    if (character.Inventory.Bag.FreeSlotCount < rowP)
                    {
                        character.SendErrorMessage(Error.ErrorMessageType.BagFull);
                        return;
                    }

                    for (var i = 1; i <= 17; i++) ////////max group here ////// in sqlite max group = 17 /////
                    {
                        var itemIdLoot = (uint)0;
                        var minAmount = 0;
                        var maxAmount = 0;
                        var gradeId = (byte)0;
                        var dropRateMax = (uint)0;
                        for (var ui = 0; ui < rowP; ui++)
                            if (lootPacks[ui].Group == i)
                                dropRateMax += lootPacks[ui].DropRate;

                        var dropRateItem = Rand.Next(0, dropRateMax);
                        var dropRateItemId = (uint)0;
                        for (var uii = 0; uii < rowP; uii++)
                        {
                            if (lootPacks[uii].Group == i)
                            {
                                if (lootPacks[uii].DropRate + dropRateItemId >= dropRateItem)
                                {
                                    itemIdLoot = lootPacks[uii].ItemId;
                                    minAmount = lootPacks[uii].MinAmount;
                                    maxAmount = lootPacks[uii].MaxAmount;
                                    gradeId = lootPacks[uii].GradeId;
                                    uii = rowP;
                                }
                                else
                                {
                                    dropRateItemId += lootPacks[uii].DropRate;
                                }
                            }
                        }

                        if (minAmount > 1 && itemIdLoot == 500)
                            AddGold(caster, minAmount, maxAmount);

                        if (itemIdLoot > 0 && itemIdLoot != 500)
                        {
                            if (InheritGrade)
                                gradeId = lootPackItem.Grade;

                            AddItem(caster, itemIdLoot, gradeId, minAmount, maxAmount, madeBy);
                        }
                    }
                }
            }

            if (ConsumeSourceItem)
                character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, lootPack.ItemTemplateId, 1, null);

            _log.Debug("GainLootPackItemEffect {0}", LootPackId);
        }

        private void AddGold(Unit caster, int goldMin, int goldMax)
        {
            var character = (Character)caster;
            if (character == null) return;
            var goldAdd = Rand.Next(goldMin, goldMax);
            var jackpot = Rand.Next(0, 10000);
            if (jackpot <= 50)
                goldAdd = goldAdd * 1000;

            if (jackpot <= 5)
                goldAdd = goldAdd * 5000;

            character.Money += goldAdd;
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillEffectGainItem,
                new List<ItemTask> {new MoneyChange(goldAdd)}, new List<ulong>()));
        }

        private void AddItem(Unit caster, uint itemId, byte gradeId, int minAmount, int maxAmount, uint crafterId)
        {
            var character = (Character)caster;
            if (character == null) return;
            var amount = Rand.Next(minAmount, maxAmount);
            if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Loot, itemId, amount, gradeId, crafterId))
            {
                // TODO: do proper handling of insufficient bag space
                character.SendErrorMessage(Error.ErrorMessageType.BagFull);
            }
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

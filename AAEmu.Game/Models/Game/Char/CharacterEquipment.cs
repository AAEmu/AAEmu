using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Char
{
    public partial class Character
    {
        public void UpdateGearBonuses(Item itemAdded, Item itemRemoved)
        {
            // We use index 1 for gear bonuses. Will make this a constant later, or do it properly. Right now the expected behavior is to have key == buff id, which doesn't work when you have items.
            Bonuses[1] = new List<Bonus>();

            foreach (var item in Equipment.Items)
            {
                if (!(item is EquipItem ei))
                    continue;

                // Gems
                foreach (var gem in ei.GemIds)
                foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
                    AddBonus(1, new Bonus {Template = template, Value = template.Value});
            }

            // Apply Equip Effects
            ApplyEquipEffects(itemAdded, itemRemoved);

            // Compute gear buff
            ApplyWeaponWieldBuff();
            ApplyArmorGradeBuff(itemAdded, itemRemoved);
            ApplyEquipItemSetBonuses();
        }

        private void ApplyWeaponWieldBuff()
        {
            Buffs.RemoveBuff((uint)BuffConstants.EquipDualwield);
            Buffs.RemoveBuff((uint)BuffConstants.EquipShield);
            Buffs.RemoveBuff((uint)BuffConstants.EquipTwoHanded);

            BuffTemplate buffTemplate = null;
            switch (GetWeaponWieldKind())
            {
                case WeaponWieldKind.None:
                case WeaponWieldKind.OneHanded:
                    var item = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                    if (item != null && item.Template is WeaponTemplate weapon)
                    {
                        var slotId = (EquipmentItemSlotType)weapon.HoldableTemplate.SlotTypeId;
                        if (slotId == EquipmentItemSlotType.Shield)
                            buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EquipShield);
                    }
                    break;
                case WeaponWieldKind.TwoHanded:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EquipTwoHanded);
                    break;
                case WeaponWieldKind.DuelWielded:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EquipDualwield);
                    break;
            }

            if(buffTemplate != null)
            {
                var effect = new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.UtcNow);
                Buffs.AddBuff(effect);
            }

        }

        private void ApplyEquipItemSetBonuses()
        {
            var setNumPieces = new Dictionary<uint, int>();
            var itemLevels = new Dictionary<uint, uint>();
            foreach(var item in Equipment.Items)
            {
                if (item.Template is EquipItemTemplate template)
                {
                    var equipItemSetId = template.EquipItemSetId;
                    if (template.EquipItemSetId == 0)
                        continue;

                    if (!setNumPieces.ContainsKey(equipItemSetId))
                    {
                        setNumPieces.Add(equipItemSetId, (1));
                        itemLevels.Add(equipItemSetId, (uint)item.Template.Level);
                    }
                    else
                    {
                        setNumPieces[equipItemSetId]++;
                        if (item.Template.Level < itemLevels[equipItemSetId])
                            itemLevels[equipItemSetId] = (uint)item.Template.Level;
                    }
                }
            }

            var appliedBuffs = new HashSet<uint>();
            foreach (var setCount in setNumPieces)
            {
                var equipItemSet = ItemManager.Instance.GetEquippedItemSet(setCount.Key);
                foreach(var bonus in equipItemSet.Bonuses)
                {
                    if (setCount.Value >= bonus.NumPieces)
                    {
                        if (bonus.BuffId != 0)
                        {
                            if (Buffs.CheckBuff(bonus.BuffId))
                            {
                                appliedBuffs.Add(bonus.BuffId);
                                continue;
                            }
                            var buffTemplate = SkillManager.Instance.GetBuffTemplate(bonus.BuffId);

                            var newEffect =
                                new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.UtcNow)
                                {
                                    AbLevel = itemLevels[setCount.Key]
                                };
                            Buffs.AddBuff(newEffect);
                            appliedBuffs.Add(bonus.BuffId);
                        }
                        if (bonus.ItemProcId != 0)
                        {
                            Procs.AddProc(bonus.ItemProcId);
                        }
                    }
                    else //This needs to be revised? Will we ever remove more than 1 item at a time?
                    {
                        if (bonus.BuffId != 0 && Buffs.CheckBuff(bonus.BuffId) && !appliedBuffs.Contains(bonus.BuffId))
                            Buffs.RemoveBuff(bonus.BuffId);
                        if (bonus.ItemProcId != 0)
                            Procs.RemoveProc(bonus.ItemProcId);
                    }
                }
            }
        }

        private void ApplyArmorGradeBuff(Item itemAdded, Item itemRemoved)
        {
            if ((itemAdded != null || itemRemoved != null) && (!(itemAdded is Armor) && !(itemRemoved is Armor)))
                return;

            // Clear any existing armor grade buffs
            Buffs.RemoveBuffs((uint) BuffConstants.ArmorBuffTag, 10);

            // Get armor pieces by kind
            var armorPieces = new Dictionary<ArmorType, List<Armor>>();
            foreach (var item in Equipment.Items)
            {
                if (!(item is Armor armor))
                    continue;

                if (!(item.Template is ArmorTemplate armorTemplate))
                    continue;

                if (armorTemplate.SlotTemplate.SlotTypeId == (ulong)EquipmentItemSlotType.Back)
                    continue;

                if (!armorPieces.ContainsKey((ArmorType)armorTemplate.KindTemplate.TypeId))
                    armorPieces.Add((ArmorType)armorTemplate.KindTemplate.TypeId, new List<Armor>());
                armorPieces[(ArmorType)armorTemplate.KindTemplate.TypeId].Add(armor);
            }

            if (!armorPieces.Any())
                return;
            // Get kind with most pieces
            var piecesOfKind = armorPieces.First();
            foreach (var piecesByKind in armorPieces)
            {
                if (piecesByKind.Value.Count > piecesOfKind.Value.Count) piecesOfKind = piecesByKind;
            }

            var piecesToAccountForBuff = piecesOfKind.Value;

            if (piecesToAccountForBuff.Count < 4)
                return;

            var finalArmorTemplate = piecesToAccountForBuff.First().Template as ArmorTemplate;
            if (finalArmorTemplate == null)
                return;
            
            if (piecesToAccountForBuff.Count == 7)
            {
                BuffTemplate templ = null;
                switch ((ArmorType)finalArmorTemplate.WearableTemplate.TypeId)
                {
                    case ArmorType.Cloth:
                        templ = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.Cloth7P);
                        break;
                    case ArmorType.Leather:
                        templ = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.Leather7P);
                        break;
                    case ArmorType.Metal:
                        templ = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.Plate7P);
                        break;
                }
                
                if (templ != null)
                    Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(), templ, null, DateTime.UtcNow));
            }
            else
            {
                BuffTemplate templ = null;
                switch ((ArmorType)finalArmorTemplate.WearableTemplate.TypeId)
                {
                    case ArmorType.Cloth:
                        templ = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.Cloth4P);
                        break;
                    case ArmorType.Leather:
                        templ = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.Leather4P);
                        break;
                    case ArmorType.Metal:
                        templ = SkillManager.Instance.GetBuffTemplate((uint) BuffConstants.Plate4P);
                        break;
                }
                
                if (templ != null)
                    Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(), templ, null, DateTime.UtcNow));
            }

            // Get only pieces >= arcane
            var piecesAboveArcane = piecesToAccountForBuff.Where(p => p.Grade >= (int)ItemGrade.Arcane).ToList();
            if (piecesAboveArcane.Count < 4)
                return;

            var totalLevel = piecesAboveArcane.Sum(a => a.Template.Level);

            // This const was calculated by hand, it might make no sense.
            var abLevel = totalLevel * 0.40670554f;
            var gradeBuffAbLevel = (abLevel * abLevel) / 15 + 30;
            var lowestGrade = piecesAboveArcane.Min(a => a.Grade);

            // Apply buff 
            if (piecesAboveArcane.First().Template is ArmorTemplate armorTemp)
            {
                var type = armorTemp.WearableTemplate.TypeId;
                var armorGradeBuff =
                    ItemManager.Instance.GetArmorGradeBuff((ArmorType)type, (ItemGrade)lowestGrade);
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(armorGradeBuff.BuffId);

                var newEffect =
                    new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                    {
                        AbLevel = (uint)gradeBuffAbLevel
                    };

                Buffs.AddBuff(newEffect);
            }
        }

        private void ApplyEquipEffects(Item itemAdded, Item itemRemoved)
        {
            if (itemRemoved != null)
            {
                // Static Item Buffs
                var itemRemovedBuff = ItemGameData.Instance.GetItemBuff(itemRemoved?.TemplateId ?? 0, itemRemoved?.Grade ?? 0);
                if (itemRemovedBuff == null)
                    itemRemovedBuff = SkillManager.Instance.GetBuffTemplate(itemRemoved?.Template.BuffId ?? 0);
                if (itemRemovedBuff != null) // remove previous buff
                {
                    if (Buffs.CheckBuff(itemRemovedBuff.Id))
                    {
                        Buffs.RemoveBuff(itemRemovedBuff.Id);
                    }
                }

                // Charged Item Buffs
                if ((itemRemoved.Template is EquipItemTemplate equipItemTemplate) &&
                    (equipItemTemplate.RechargeBuffId > 0) &&
                    Buffs.CheckBuff(equipItemTemplate.RechargeBuffId))
                    Buffs.RemoveBuff(equipItemTemplate.RechargeBuffId);
            }

            if(itemAdded != null)
            {
                // Static Buffs
                var itemAddedBuff = ItemGameData.Instance.GetItemBuff(itemAdded?.TemplateId ?? 0, itemAdded?.Grade ?? 0);
                if (itemAddedBuff == null)
                    itemAddedBuff = SkillManager.Instance.GetBuffTemplate(itemAdded?.Template.BuffId ?? 0);
                if (itemAddedBuff != null) // add buff from equipped item
                {
                    var newEffect =
                        new Buff(this, this, new SkillCasterUnit(), itemAddedBuff, null, DateTime.UtcNow)
                        {
                            AbLevel = (uint)itemAdded.Template.Level
                        };

                    Buffs.AddBuff(newEffect);
                }

                // Charged Item Buffs
                if ((itemAdded is EquipItem equipItem) && (equipItem.Template is EquipItemTemplate equipItemTemplate) &&
                    (equipItemTemplate.RechargeBuffId > 0))
                {
                    var addChargeBuff = false;
                    var checkExpireTime = equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack)
                        ? equipItem.UnpackTime
                        : equipItem.ChargeStartTime;
                    checkExpireTime = checkExpireTime.AddMinutes(equipItemTemplate.ChargeLifetime);
                    
                    // Check against timer
                    if ((equipItemTemplate.ChargeLifetime > 0) && (checkExpireTime > DateTime.UtcNow))
                        addChargeBuff = true;

                    // Check against charge counter
                    if ((equipItemTemplate.ChargeCount > 0) && (equipItem.ChargeCount > 0))
                        addChargeBuff = true;

                    // If this item is Bind on unwrap, don't start the buff if it's not unwrapped
                    if (equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) && (equipItem.HasFlag(ItemFlag.Unpacked) == false))
                        addChargeBuff = false;

                    if (addChargeBuff)
                    {
                        var itemAddedChargedBuff = SkillManager.Instance.GetBuffTemplate(equipItemTemplate.RechargeBuffId);
                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(), itemAddedChargedBuff, null, DateTime.UtcNow)
                            {
                                AbLevel = (uint)itemAdded.Template.Level
                            };
                        Buffs.AddBuff(newEffect);
                    }
                }
            }

            if(itemAdded == null && itemRemoved == null) // This is the first load check to apply buffs for equipped items. 
            {
                Buffs.RemoveBuffs((uint)BuffConstants.EquipmentBuffTag, 20);
                foreach (var item in Equipment.Items)
                {
                    // Static Buffs
                    if(item.Template.BuffId != 0)
                    {
                        var buffTemplate = ItemGameData.Instance.GetItemBuff(item?.TemplateId ?? 0, item?.Grade ?? 0);
                        if (buffTemplate == null)
                            buffTemplate = SkillManager.Instance.GetBuffTemplate(item?.Template.BuffId ?? 0);
                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                            {
                                AbLevel = (uint)item.Template.Level
                            };

                        Buffs.AddBuff(newEffect);
                    }
                    
                    // Charged Item Buffs
                    if ((item is EquipItem equipItem) && (equipItem.Template is EquipItemTemplate equipItemTemplate) &&
                        (equipItemTemplate.RechargeBuffId > 0))
                    {
                        var addChargeBuff = false;
                        var checkExpireTime = equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack)
                            ? equipItem.UnpackTime
                            : equipItem.ChargeStartTime;
                        checkExpireTime = checkExpireTime.AddMinutes(equipItemTemplate.ChargeLifetime);
                    
                        // Check against timer
                        if ((equipItemTemplate.ChargeLifetime > 0) && (checkExpireTime > DateTime.UtcNow))
                            addChargeBuff = true;

                        // Check against charge counter
                        if ((equipItemTemplate.ChargeCount > 0) && (equipItem.ChargeCount > 0))
                            addChargeBuff = true;

                        // If this item is Bind on unwrap, don't start the buff if it's not unwrapped
                        if (equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) && (equipItem.HasFlag(ItemFlag.Unpacked) == false))
                            addChargeBuff = false;

                        if (addChargeBuff)
                        {
                            var itemAddedChargedBuff = SkillManager.Instance.GetBuffTemplate(equipItemTemplate.RechargeBuffId);
                            var newEffect =
                                new Buff(this, this, new SkillCasterUnit(), itemAddedChargedBuff, null, DateTime.UtcNow)
                                {
                                    AbLevel = (uint)item.Template.Level
                                };
                            Buffs.AddBuff(newEffect);
                        }
                    }
                }
            }
        }
    }
}

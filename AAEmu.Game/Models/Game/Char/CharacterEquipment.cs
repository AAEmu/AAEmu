using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
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
            ApplyArmorGradeBuff();
            ApplyEquipItemSetBonuses();
        }

        private void ApplyWeaponWieldBuff()
        {
            Buffs.RemoveBuff((uint)BuffConstants.EQUIP_DUALWIELD_BUFF);
            Buffs.RemoveBuff((uint)BuffConstants.EQUIP_SHIELD_BUFF);
            Buffs.RemoveBuff((uint)BuffConstants.EQUIP_TWOHANDED_BUFF);

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
                            buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EQUIP_SHIELD_BUFF);
                    }
                    break;
                case WeaponWieldKind.TwoHanded:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EQUIP_TWOHANDED_BUFF);
                    break;
                case WeaponWieldKind.DuelWielded:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EQUIP_DUALWIELD_BUFF);
                    break;
            }

            if(buffTemplate != null)
            {
                var effect = new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.Now);
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
                var equipItemSet = ItemManager.Instance.GetEquiptItemSet(setCount.Key);
                foreach(var bonus in equipItemSet.Bonuses)
                {
                    if (setCount.Value >= bonus.NumPieces)
                    {
                        if (Buffs.CheckBuff(bonus.BuffId))
                        {
                            appliedBuffs.Add(bonus.BuffId);
                            continue;
                        }
                        var buffTemplate = SkillManager.Instance.GetBuffTemplate(bonus.BuffId);

                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.Now)
                            {
                                AbLevel = itemLevels[setCount.Key]
                            };
                        Buffs.AddBuff(newEffect);
                        appliedBuffs.Add(bonus.BuffId);
                    }
                    else //This needs to be revised? Will we ever remove more than 1 item at a time?
                    {
                        if (Buffs.CheckBuff(bonus.BuffId) && !appliedBuffs.Contains(bonus.BuffId))
                            Buffs.RemoveBuff(bonus.BuffId);
                    }
                }
            }
        }

        private void ApplyArmorGradeBuff()
        {
            // Clear any existing armor grade buffs
            Buffs.RemoveBuffs(145, 1);
            
            // Get armor pieces by kind
            var armorPieces = new Dictionary<ArmorType, List<Armor>>();
            foreach (var item in Equipment.Items)
            {
                if (!(item is Armor armor))
                    continue;

                if (!(item.Template is ArmorTemplate armorTemplate))
                    continue;

                if (armorTemplate.SlotTemplate.SlotTypeId == (ulong) EquipmentItemSlotType.Back)
                    continue;

                if (!armorPieces.ContainsKey((ArmorType)armorTemplate.KindTemplate.TypeId))
                    armorPieces.Add((ArmorType)armorTemplate.KindTemplate.TypeId, new List<Armor>());
                armorPieces[(ArmorType)armorTemplate.KindTemplate.TypeId].Add(armor);
            }
            if (armorPieces.Count() == 0)
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
            
            // Get only pieces >= arcane
            var piecesAboveArcane = piecesToAccountForBuff.Where(p => p.Grade >= (int) ItemGrade.Arcane).ToList();
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
                var armorGradeBuff = ItemManager.Instance.GetArmorGradeBuff((ArmorType)type, (ItemGrade) lowestGrade);
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(armorGradeBuff.BuffId);
                
                var newEffect =
                    new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.Now)
                    {
                        AbLevel = (uint) gradeBuffAbLevel
                    };

                Buffs.AddBuff(newEffect);
            }
        }

        private void ApplyEquipEffects(Item itemAdded, Item itemRemoved)
        {
            if(itemRemoved != null && itemRemoved.Template.BuffId != 0) // remove previous buff
            {
                if(Buffs.CheckBuff(itemRemoved.Template.BuffId))
                {
                    Buffs.RemoveBuff(itemRemoved.Template.BuffId);
                }
            }

            if(itemAdded != null && itemAdded.Template.BuffId != 0) // add buff from equipped item
            {
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(itemAdded.Template.BuffId);
                var newEffect =
                    new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                    {
                        AbLevel = 1
                    };

                Buffs.AddBuff(newEffect);
            }

            if(itemAdded == null && itemRemoved == null) // This is the first load check to apply buffs for equipped items. 
            {
                foreach (var item in Equipment.Items)
                {
                    if(item.Template.BuffId != 0)
                    {
                        var buffTemplate = SkillManager.Instance.GetBuffTemplate(item.Template.BuffId);
                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                            {
                                AbLevel = 1
                            };

                        Buffs.AddBuff(newEffect);
                    }
                }
            }
        }
    }
}

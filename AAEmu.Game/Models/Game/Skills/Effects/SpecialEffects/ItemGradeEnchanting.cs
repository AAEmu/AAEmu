using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class ItemGradeEnchanting : ISpecialEffect
    {
        private enum GradeEnchantResult {
            Break = 0,
            Downgrade = 1,
            Fail = 2,
            Success = 3,
            GreatSuccess = 4
        }

        protected static Logger _log = LogManager.GetCurrentClassLogger(); 

        public void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3, int Value4)
        {
            Character character = (Character) caster;
            if (character == null) return;

            SkillItem scroll = (SkillItem) casterObj;
            if (scroll == null) return;

            SkillCastUnk3Target itemTarget = (SkillCastUnk3Target) targetObj;
            if (itemTarget == null) return;

            var useCharm = false;
            SkillObjectItemGradeEnchantingSupport charm = (SkillObjectItemGradeEnchantingSupport) skillObject;
            if (charm != null && charm.SupportItemId != 0) useCharm = true;
            
            bool isLucky = Value1 != 0;
            Item item = character.Inventory.GetItem(itemTarget.Id);
            var initialGrade = item.Grade;
            var gradeTemplate = ItemManager.Instance.GetGradeTemplate(item.Grade);
            var tasks = new List<ItemTask>();

            int cost = GoldCost(gradeTemplate, item, Value3);
            if (cost == -1) return;

            if (character.Money < cost) return;
            character.Money -= cost;
            tasks.Add(new MoneyChange(-cost));

            if (!character.Inventory.CheckItems(scroll.ItemTemplateId, 1)) return;
            Item scrollItem = character.Inventory.GetItem(scroll.ItemId);
            tasks.Add(InventoryHelper.GetTaskAndRemoveItem(character, scrollItem, 1));
        
            ItemGradeEnchantingSupport charmInfo = null;
            if (useCharm) { 
                Item charmItem = character.Inventory.GetItem(charm.SupportItemId);
                if (charmItem == null) return;

                charmInfo = ItemManager.Instance.GetItemGradEnchantingSupportByItemId(charmItem.TemplateId);

                if (charmInfo.RequireGradeMin != -1 && item.Grade < charmInfo.RequireGradeMin) return;
                if (charmInfo.RequireGradeMax != -1 && item.Grade > charmInfo.RequireGradeMax) return;

                tasks.Add(InventoryHelper.GetTaskAndRemoveItem(character, charmItem, 1));
            }

            var result = RollRegrade(gradeTemplate, item, isLucky, useCharm, charmInfo);

            if (result == GradeEnchantResult.Break) {
                tasks.Add(InventoryHelper.GetTaskAndRemoveItem(character, item, 1));
            } else {
                // TODO : make sure grade updates with itemupdate
                tasks.Add(new ItemGradeChange(item, item.Grade));
            }
            
            character.SendPacket(new SCGradeEnchantResultPacket((byte)result, item, initialGrade, item.Grade));
            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GradeEnchant, tasks, new List<ulong>()));

            if (item.Grade >= 8 && (result == GradeEnchantResult.Success || result == GradeEnchantResult.GreatSuccess)) {
                WorldManager.Instance.BroadcastPacketToServer(new SCGradeEnchantBroadcastPacket(character.Name, (byte)result, item, initialGrade, item.Grade));
            }
        }

        private GradeEnchantResult RollRegrade(GradeTemplate gradeTemplate, Item item, bool isLucky, bool useCharm, ItemGradeEnchantingSupport charmInfo)
        {
            var successRoll = Rand.Next(0, 10000);
            var breakRoll = Rand.Next(0, 10000);
            var downgradeRoll = Rand.Next(0, 10000);
            var greatSuccessRoll = Rand.Next(0, 10000);

            // TODO : Refactor
            var successChance = useCharm ? GetCharmChance(gradeTemplate.EnchantSuccessRatio, charmInfo.AddSuccessRatio, charmInfo.AddSuccessMul) : gradeTemplate.EnchantSuccessRatio;
            var greatSuccessChance = useCharm ? GetCharmChance(gradeTemplate.EnchantGreatSuccessRatio, charmInfo.AddGreatSuccessRatio, charmInfo.AddGreatSuccessMul) : gradeTemplate.EnchantGreatSuccessRatio;
            var breakChance = useCharm ? GetCharmChance(gradeTemplate.EnchantBreakRatio, charmInfo.AddBreakRatio, charmInfo.AddBreakMul) : gradeTemplate.EnchantBreakRatio;
            var downgradeChance = useCharm ? GetCharmChance(gradeTemplate.EnchantDowngradeRatio, charmInfo.AddDowngradeRatio, charmInfo.AddDowngradeMul) : gradeTemplate.EnchantDowngradeRatio;

            if (successRoll < successChance) {
                if (isLucky && greatSuccessRoll < greatSuccessChance) {
                    // TODO : Refactor
                    var increase = useCharm ? 2 + charmInfo.AddGreatSuccessGrade : 2;
                    item.Grade = (byte) GetNextGrade(gradeTemplate, increase).Grade;
                    return GradeEnchantResult.GreatSuccess;
                } else {
                    item.Grade = (byte) GetNextGrade(gradeTemplate, 1).Grade;
                    return GradeEnchantResult.Success;
                }
            } else {
                if (breakRoll < breakChance) {
                    return GradeEnchantResult.Break;
                } else {
                    if (downgradeRoll < downgradeChance) {
                        var newGrade = (byte) Rand.Next(gradeTemplate.EnchantDowngradeMin, gradeTemplate.EnchantDowngradeMax);
                        if (newGrade < 0) return GradeEnchantResult.Fail;

                        item.Grade = newGrade;
                        return GradeEnchantResult.Downgrade;
                    } else {
                        return GradeEnchantResult.Fail;
                    }
                }
            }
        }

        private int GoldCost(GradeTemplate gradeTemplate, Item item, int ItemType) {
            int cost = 0;

            uint slotTypeId = 0;
            switch (ItemType) {
                case 1:
                    WeaponTemplate weaponTemplate = (WeaponTemplate) item.Template;
                    _log.Info(weaponTemplate.HoldableTemplate.SlotTypeId);
                    slotTypeId = weaponTemplate.HoldableTemplate.SlotTypeId;
                    break;
                case 2:
                    ArmorTemplate armorTemplate = (ArmorTemplate) item.Template;
                    _log.Info(armorTemplate.SlotTemplate.SlotTypeId);
                    slotTypeId = armorTemplate.SlotTemplate.SlotTypeId;
                    break;
                case 24:
                    AccessoryTemplate accessoryTemplate = (AccessoryTemplate) item.Template;
                    _log.Info(accessoryTemplate.SlotTemplate.SlotTypeId);
                    slotTypeId = accessoryTemplate.SlotTemplate.SlotTypeId;
                    break;
            }
            
            if (slotTypeId == 0) return -1;
            EquipSlotEnchantingCost enchantingCost = ItemManager.Instance.GetEquipSlotEnchantingCost(slotTypeId);
            
            int item_grade = gradeTemplate.EnchantCost;
            int item_level = item.Template.Level;
            int equip_slot_enchant_cost = enchantingCost.Cost;
            
            Dictionary<string, double> parameters = new Dictionary<string, double>();
            parameters.Add("item_grade", item_grade);
            parameters.Add("item_level", item_level);
            parameters.Add("equip_slot_enchant_cost", equip_slot_enchant_cost);
            Formula formula = FormulaManager.Instance.GetFormula(22);

            cost = (int)formula.Evaluate(parameters);

            return cost;
        }

        // This is required because XLGames are very smart! Instead of being logical and ordering the item_grades table, they added grade_order column...
        private GradeTemplate GetNextGrade(GradeTemplate currentGrade, int gradeChange) {
            return ItemManager.Instance.GetGradeTemplateByOrder(currentGrade.GradeOrder + gradeChange);
        }

        private int GetCharmChance(int baseChance, int charmRatio, int charmMul) {
            return (baseChance + charmRatio) + (int)(baseChance * (charmMul / 100.0));
        }
    }
}

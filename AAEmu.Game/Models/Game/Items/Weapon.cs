using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class Weapon : EquipItem
    {
        public override int Str
        {
            get
            {
                var template = (WeaponTemplate)Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float)Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetHoldableStatConst() * 0.0099999998f;
                var temp2 = (modifiers.Count * temp * modifiers.StrWeight * 1f) / modifiers.AllWeight *
                            grade.StatMultiplier * 0.0099999998f + 0.5f;
                var res = (int)temp2 * template.HoldableTemplate.StatMultiplier * 0.0099999998f + 0.5f;
                return (int)res;
            }
        }

        public override int Dex
        {
            get
            {
                var template = (WeaponTemplate)Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float)Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetHoldableStatConst() * 0.0099999998f;
                var temp2 = (modifiers.Count * temp * modifiers.DexWeight * 1f) / modifiers.AllWeight *
                            grade.StatMultiplier * 0.0099999998f + 0.5f;
                var res = (int)temp2 * template.HoldableTemplate.StatMultiplier * 0.0099999998f + 0.5f;
                return (int)res;
            }
        }

        public override int Sta
        {
            get
            {
                var template = (WeaponTemplate)Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float)Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetHoldableStatConst() * 0.0099999998f;
                var temp2 = (modifiers.Count * temp * modifiers.StaWeight * 1f) / modifiers.AllWeight *
                            grade.StatMultiplier * 0.0099999998f + 0.5f;
                var res = (int)temp2 * template.HoldableTemplate.StatMultiplier * 0.0099999998f + 0.5f;
                return (int)res;
            }
        }

        public override int Int
        {
            get
            {
                var template = (WeaponTemplate)Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float)Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetHoldableStatConst() * 0.0099999998f;
                var temp2 = (modifiers.Count * temp * modifiers.IntWeight * 1f) / modifiers.AllWeight *
                            grade.StatMultiplier * 0.0099999998f + 0.5f;
                var res = (int)temp2 * template.HoldableTemplate.StatMultiplier * 0.0099999998f + 0.5f;
                return (int)res;
            }
        }

        public override int Spi
        {
            get
            {
                var template = (WeaponTemplate)Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float)Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetHoldableStatConst() * 0.0099999998f;
                var temp2 = (modifiers.Count * temp * modifiers.SpiWeight * 1f) / modifiers.AllWeight *
                            grade.StatMultiplier * 0.0099999998f + 0.5f;
                var res = (int)temp2 * template.HoldableTemplate.StatMultiplier * 0.0099999998f + 0.5f;
                return (int)res;
            }
        }

        public float Dps
        {
            get
            {
                var template = (WeaponTemplate)Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var formula = template.HoldableTemplate.FormulaDps;
                var parameters = new Dictionary<string, double>();
                parameters["item_level"] = template.Level;
                parameters["item_grade"] = grade.HoldableDps;
                return (float)formula.Evaluate(parameters);
            }
        }

        public double MDps
        {
            get
            {
                var template = (WeaponTemplate)Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var formula = template.HoldableTemplate.FormulaMDps;
                var parameters = new Dictionary<string, double>();
                parameters["item_level"] = template.Level;
                parameters["item_grade"] = grade.HoldableMagicDps;
                return formula.Evaluate(parameters);
            }
        }
        
        public double HDps
        {
            get
            {
                var template = (WeaponTemplate)Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var formula = template.HoldableTemplate.FormulaHDps;
                var parameters = new Dictionary<string, double>();
                parameters["item_level"] = template.Level;
                parameters["item_grade"] = grade.HoldableMagicDps;
                return formula.Evaluate(parameters);
            }
        }

        public int Armor
        {
            get
            {
                var template = (WeaponTemplate)Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var formula = template.HoldableTemplate.FormulaArmor;
                var parameters = new Dictionary<string, double>();
                parameters["item_level"] = template.Level;
                parameters["item_grade"] = grade.HoldableArmor;
                return (int)formula.Evaluate(parameters);
            }
        }

        public sealed override byte MaxDurability
        {
            get
            {
                var template = (WeaponTemplate)Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var durability =
                    (int)((int)(ItemManager.Instance.GetHoldableDurabilityConst() * 100 + 0.5f) *
                          (int)(template.HoldableTemplate.DurabilityRatio * 1000 + 0.5f) * grade.Durability * 1000 *
                          0.00000001f) * ItemManager.Instance.GetDurabilityConst();
                durability = (float)Math.Round(durability * template.DurabilityMultiplier * 0.0099999998f);
                return (byte)durability;
            }
        }

        public Weapon()
        {
        }

        public Weapon(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
            Durability = MaxDurability;
        }
    }
}

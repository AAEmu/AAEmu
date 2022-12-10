using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public enum ArmorType : byte
    {
        Cloth = 1,
        Leather = 2,
        Metal = 3,
        PetArmor = 4,
        Etc = 5
    }

    public class Armor : EquipItem
    {
        public override int Str
        {
            get
            {
                var template = (ArmorTemplate) Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float) Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetWearableStatConst() * 0.0099999998f *
                           template.SlotTemplate.Coverage * 0.01f;
                var res = (modifiers.Count * temp * modifiers.StrWeight) * 1f / modifiers.AllWeight *
                          grade.StatMultiplier * 0.0099999998f + 0.5f;
                return (int) res;
            }
        }

        public override int Dex
        {
            get
            {
                var template = (ArmorTemplate) Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float) Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetWearableStatConst() * 0.0099999998f *
                           template.SlotTemplate.Coverage * 0.01f;
                var res = (modifiers.Count * temp * modifiers.DexWeight) * 1f / modifiers.AllWeight *
                          grade.StatMultiplier * 0.0099999998f + 0.5f;
                return (int) res;
            }
        }

        public override int Sta
        {
            get
            {
                var template = (ArmorTemplate) Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float) Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetWearableStatConst() * 0.0099999998f *
                           template.SlotTemplate.Coverage * 0.01f;
                var res = (modifiers.Count * temp * modifiers.StaWeight) * 1f / modifiers.AllWeight *
                          grade.StatMultiplier * 0.0099999998f + 0.5f;
                return (int) res;
            }
        }

        public override int Int
        {
            get
            {
                var template = (ArmorTemplate) Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float) Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetWearableStatConst() * 0.0099999998f *
                           template.SlotTemplate.Coverage * 0.01f;
                var res = (modifiers.Count * temp * modifiers.IntWeight) * 1f / modifiers.AllWeight *
                          grade.StatMultiplier * 0.0099999998f + 0.5f;
                return (int) res;
            }
        }

        public override int Spi
        {
            get
            {
                var template = (ArmorTemplate) Template;
                if (template.ModSetId == 0)
                    return 0;
                var modifiers = ItemManager.Instance.GetAttributeModifiers(template.ModSetId);
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var mod = 1f;
                if (modifiers.Count == 1)
                    mod = 3f;
                if (modifiers.Count == 2)
                    mod = 1.5f;
                mod = (float) Math.Pow(mod, 1f / (ItemManager.Instance.GetStatValueConst() * 0.0099999998f));
                var temp = ItemManager.Instance.GetItemStatConst() * 0.0099999998f * template.Level * mod *
                           ItemManager.Instance.GetWearableStatConst() * 0.0099999998f *
                           template.SlotTemplate.Coverage * 0.01f;
                var res = (modifiers.Count * temp * modifiers.SpiWeight) * 1f / modifiers.AllWeight *
                          grade.StatMultiplier * 0.0099999998f + 0.5f;
                return (int) res;
            }
        }

        public int BaseArmor
        {
            get
            {
                var template = (ArmorTemplate) Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var formula = FormulaManager.Instance.GetWearableFormula(WearableFormulaType.MaxBaseArmor);
                var parameters = new Dictionary<string, double>();
                parameters["item_level"] = template.Level;
                parameters["item_grade"] = grade.WearableArmor;
                var res = formula.Evaluate(parameters);
                res = res * template.KindTemplate.ArmorRatio * 0.0099999998f;
                if (TemperPhysical > 100)
                    res = res * (TemperPhysical / 100.0f);
                return (int) (res * template.SlotTemplate.Coverage * 0.0099999998f);
            }
        }

        public int BaseMagicResistance
        {
            get
            {
                var template = (ArmorTemplate) Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var formula = FormulaManager.Instance.GetWearableFormula(WearableFormulaType.MaxBaseMagicResistance);
                var parameters = new Dictionary<string, double>();
                parameters["item_level"] = template.Level;
                parameters["item_grade"] = grade.WearableMagicResistance;
                var res = formula.Evaluate(parameters);
                res = res * template.KindTemplate.MagicResistanceRatio * 0.0099999998f;
                if (TemperMagical > 100)
                    res = res * (TemperMagical / 100.0f);
                return (int) (res * template.SlotTemplate.Coverage * 0.0099999998f);
            }
        }

        public sealed override byte MaxDurability
        {
            get
            {
                var template = (ArmorTemplate) Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var durability =
                    (int) ((int) (ItemManager.Instance.GetWearableDurabilityConst() * 1000 + 0.5f) *
                           (int) (template.SlotTemplate.Coverage * 100 + 0.5f) * template.KindTemplate.DurabilityRatio *
                           1000 * 1.0e-10f) * ItemManager.Instance.GetDurabilityConst() * grade.Durability;
                durability = (float) Math.Round(durability * template.DurabilityMultiplier * 0.0099999998f);
                return (byte) durability;
            }
        }

        public Armor()
        {
        }

        public Armor(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
            Durability = MaxDurability;
        }
    }
}

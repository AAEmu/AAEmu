using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils;
using static AAEmu.Game.Models.Game.Skills.SkillControllers.SkillController;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class Npc : Unit
    {
        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Npc;
        //public uint TemplateId { get; set; } // moved to BaseUnit
        public NpcTemplate Template { get; set; }
        //public Item[] Equip { get; set; }
        public NpcSpawner Spawner { get; set; }

        public override UnitCustomModelParams ModelParams => Template.ModelParams;
        public override float Scale => Template.Scale;

        public override byte RaceGender => (byte)(16 * Template.Gender + Template.Race);

        public NpcAi Ai { get; set; } // New framework
        public ConcurrentDictionary<uint, Aggro> AggroTable { get; }
        public uint CurrentAggroTarget { get; set; }

        #region Attributes
        [UnitAttribute(UnitAttribute.Str)]
        public int Str
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Str);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Str))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Dex)]
        public int Dex
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Dex);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Dex))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Sta)]
        public int Sta
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Sta);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Sta))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Int)]
        public int Int
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Int);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Int))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Spi)]
        public int Spi
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Spi))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Fai)]
        public int Fai
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Fai);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Fai))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.MaxHealth)]
        public override int MaxHp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.MaxHealth);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.HealthRegen)]
        public override int HpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.HealthRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
        public override int PersistentHpRegen
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.PersistentHealthRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.MaxMana)]
        public override int MaxMp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.MaxMana);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.ManaRegen)]
        public override int MpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.ManaRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.PersistentManaRegen)]
        public override int PersistentMpRegen
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.PersistentManaRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        public override float LevelDps
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.LevelDps);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["ab_level"] = 0;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = formula.Evaluate(parameters);
                return (float)res;
            }
        }

        [UnitAttribute(UnitAttribute.MainhandDps)]
        public override int Dps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
                var res = weapon?.Dps ?? 0;
                res += Str / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.MainhandDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.MeleeDpsInc)]
        public override int DpsInc
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.MeleeDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        [UnitAttribute(UnitAttribute.OffhandDps)]
        public override int OffhandDps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                var res = weapon?.Dps ?? 0;
                res += Str / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.OffhandDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.RangedDps)]
        public override int RangedDps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
                var res = weapon?.Dps ?? 0;
                res += Dex / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.RangedDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.RangedDpsInc)]
        public override int RangedDpsInc
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.RangedDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.RangedDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        [UnitAttribute(UnitAttribute.SpellDps)]
        public override int MDps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
                var res = weapon?.MDps ?? 0;
                res += Int / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.SpellDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.SpellDpsInc)]
        public override int MDpsInc
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.SpellDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        [UnitAttribute(UnitAttribute.Armor)]
        public override int Armor
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Armor);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Armor))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.MagicResist)]
        public override int MagicResistance
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.MagicResist);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MagicResist))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        public int KillExp
        {
            get
            {
                if (Template.NoExp)
                    return 0;
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.KillExp);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["npc_template"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
                parameters["npc_kind"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
                parameters["npc_grade"] =
                    FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
                var res = formula.Evaluate(parameters);
                res *= Template.ExpMultiplier;
                res += Template.ExpAdder;
                return (int)res;
            }
        }

        #endregion

        public Npc()
        {
            Name = "";
            AggroTable = new ConcurrentDictionary<uint, Aggro>();
            //Equip = new Item[28];
        }

        public override void DoDie(Unit killer, KillReason killReason)
        {
            base.DoDie(killer, killReason);
            AggroTable.Clear();
            if (killer is Character character)
            {
                character.AddExp(KillExp, true);
                character.Quests.OnKill(this);
            }

            Spawner?.DecreaseCount(this);
            Ai?.GoToDead();
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            
            base.AddVisibleObject(character);
        }

        public override void RemoveVisibleObject(Character character)
        {
            base.RemoveVisibleObject(character);

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }

        public void AddUnitAggro(AggroKind kind, Unit unit, int amount)
        {
            amount = (int)(amount * (unit.AggroMul / 100.0f));
            amount = (int)(amount * (IncomingAggroMul / 100.0f));

            if (AggroTable.TryGetValue(unit.ObjId, out var aggro))
            {
                aggro.AddAggro(kind, amount);
            }
            else
            {
                aggro = new Aggro(unit);
                aggro.AddAggro(AggroKind.Heal, amount);
                if (AggroTable.TryAdd(unit.ObjId, aggro))
                {
                    unit.Events.OnHealed += OnAbuserHealed;
                    unit.Events.OnDeath += OnAbuserDied;
                }
            }
        }

        public void ClearAggroOfUnit(Unit unit)
        {
            if(AggroTable.TryRemove(unit.ObjId, out var value))
            {
                unit.Events.OnHealed -= OnAbuserHealed;
                unit.Events.OnDeath -= OnAbuserDied;
            }
            else
            {
                _log.Warn("Failed to remove unit[{0}] aggro from NPC[{1}]", unit.ObjId, this.ObjId);
            }
        }

        public void ClearAllAggro()
        {
            foreach(var table in AggroTable)
            {
                var unit = WorldManager.Instance.GetUnit(table.Key);
                if (unit != null)
                {
                    unit.Events.OnHealed -= OnAbuserHealed;
                    unit.Events.OnDeath -= OnAbuserDied;
                }
            }

            AggroTable.Clear();
        }

        public void OnAbuserHealed(object sender, OnHealedArgs args)
        {
            AddUnitAggro(AggroKind.Heal, args.Healer, args.HealAmount);
        }

        public void OnAbuserDied(object sender, OnDeathArgs args)
        {
            ClearAggroOfUnit(args.Victim);
        }

        public void OnDamageReceived(Unit attacker, int amount)
        {
            // 25 means "dummy" AI -> should not respond!
            // if (Template.AiFileId != 25 && (Patrol == null || Patrol.PauseAuto(this)))
            // {
            //     CurrentTarget = attacker;
            //     BroadcastPacket(new SCCombatEngagedPacket(attacker.ObjId), true); // caster
            //     BroadcastPacket(new SCCombatEngagedPacket(ObjId), true);    // target
            //     BroadcastPacket(new SCCombatFirstHitPacket(ObjId, attacker.ObjId, 0), true);
            //     BroadcastPacket(new SCAggroTargetChangedPacket(ObjId, attacker.ObjId), true);
            //     BroadcastPacket(new SCTargetChangedPacket(ObjId, attacker.ObjId), true);
            //
            //     // TaskManager.Instance.Schedule(new UnitMove(new Track(), this), TimeSpan.FromMilliseconds(100));
            // }
            AddUnitAggro(AggroKind.Damage, attacker, amount);
            Ai.OnAggroTargetChanged();

            /*var topAbuser = AggroTable.GetTopTotalAggroAbuserObjId();
            if ((CurrentTarget?.ObjId ?? 0) != topAbuser)
            {
                CurrentAggroTarget = topAbuser; 
                var unit = WorldManager.Instance.GetUnit(topAbuser);
                SetTarget(unit);
                Ai?.OnAggroTargetChanged();
            }*/
        }

        public void MoveTowards(Vector3 other, float distance, byte flags = 4)
        {
            if (ActiveSkillController != null && ActiveSkillController.State != SCState.Ended)
                return;

            var oldPosition = Transform.Local.ClonePosition();

            var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
            if (targetDist <= 0.05f)
                return;
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            var travelDist = Math.Min(targetDist, distance);

            // TODO: Implement proper use for Transform.World.AddDistanceToFront)
            var (newX, newY, newZ) = Transform.Local.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);

            // TODO: Implement Transform.World to do proper movement
            //Transform.Local.SetPosition(newX, newY, WorldManager.Instance.GetHeight(Transform));
            Transform.Local.SetPosition(newX, newY, newZ);

            var angle = MathUtil.CalculateAngleFrom(Transform.Local.Position, other);
            var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
            Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
            var (rx, ry, rz) = Transform.Local.ToRollPitchYawSBytesMovement();

            moveType.X = Transform.Local.Position.X;
            moveType.Y = Transform.Local.Position.Y;
            moveType.Z = Transform.Local.Position.Z;
            moveType.VelX = (short) velX;
            moveType.VelY = (short) velY;
            moveType.RotationX = rx;
            moveType.RotationY = ry;
            moveType.RotationZ = rz;
            moveType.ActorFlags = flags;     // 5-walk, 4-run, 3-stand still
            moveType.Flags = 4;
            
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

            CheckMovedPosition(oldPosition);
            //SetPosition(Position);
            BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), false);
        }
        
        public void LookTowards(Vector3 other, byte flags = 4)
        {
            var oldPosition = Transform.Local.ClonePosition();

            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            var angle = MathUtil.CalculateAngleFrom(Transform.Local.Position, other);
            //var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);

            // TODO: Implement Transform.World to do proper movement
            Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
            var (rx, ry, rz) = Transform.Local.ToRollPitchYawSBytesMovement();

            moveType.X = Transform.Local.Position.X;
            moveType.Y = Transform.Local.Position.Y;
            moveType.Z = Transform.Local.Position.Z;
            moveType.RotationX = rx;
            moveType.RotationY = ry;
            moveType.RotationZ = rz;
            moveType.ActorFlags = flags;     // 5-walk, 4-run, 3-stand still
            moveType.Flags = 4;
            
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 0;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

            CheckMovedPosition(oldPosition);
            //SetPosition(Position);
            BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), false);
        }
        
        public void StopMovement()
        {
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            moveType.X = Transform.Local.Position.X;
            moveType.Y = Transform.Local.Position.Y;
            moveType.Z = Transform.Local.Position.Z;
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = Transform.Local.ToRollPitchYawSBytesMovement().Item3;
            moveType.Flags = 4;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 0;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = (sbyte) (CurrentAggroTarget > 0 ? 0 : 1);    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;
            BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), false);
        }

        public override void OnSkillEnd(Skill skill)
        {
            // AI?.OnSkillEnd(skill);
        }

        public void SetTarget(Unit other)
        {
            CurrentTarget = other;
            SendPacket(new SCAggroTargetChangedPacket(ObjId, other?.ObjId ?? 0));
            BroadcastPacket(new SCTargetChangedPacket(ObjId, other?.ObjId ?? 0), true);
        }
    
        public void DoDespawn(Npc npc)
        {
            Spawner.DoDespawn(npc);
        }
    }
}

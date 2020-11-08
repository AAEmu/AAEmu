using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class Npc : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Npc;
        public uint TemplateId { get; set; }
        public NpcTemplate Template { get; set; }
        //public Item[] Equip { get; set; }
        public NpcSpawner Spawner { get; set; }

        public override UnitCustomModelParams ModelParams => Template.ModelParams;
        public override float Scale => Template.Scale;

        public override byte RaceGender => (byte)(16 * Template.Gender + Template.Race);

        #region Attributes

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
            //Equip = new Item[28];
        }

        public override void DoDie(Unit killer)
        {
            base.DoDie(killer);

            if (killer is Character character)
            {
                character.AddExp(KillExp, true);
                character.Quests.OnKill(this);
            }

            Spawner?.DecreaseCount(this);
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.NPChar;

public class Npc : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Npc;
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public NpcTemplate Template { get; set; }
    //public Item[] Equip { get; set; }
    public NpcSpawner Spawner { get; set; }
    public Gimmick Gimmick { get; set; }

    public override UnitCustomModelParams ModelParams => Template.ModelParams;
    public override float Scale => Template.Scale;

    public override byte RaceGender => (byte)(16 * Template.Gender + Template.Race);

    public NpcAi Ai { get; set; } // New framework
    public ConcurrentDictionary<uint, Aggro> AggroTable { get; }
    public uint CurrentAggroTarget { get; set; }
    public bool CanFly { get; set; } // TODO mark Npc's that can fly so that they don't land on the ground when calculating the Z height

    public override float BaseMoveSpeed
    {
        get
        {
            var model = ModelManager.Instance.GetActorModel(Template.ModelId);
            if (model == null)
                return 1f;
            // TODO: Implement stance switching mechanic
            if (!model.Stances.TryGetValue(CurrentGameStance, out var stance))
                return 1f;

            // Returning? Use sprint speed
            if (Ai?.GetCurrentBehavior() is ReturnStateBehavior rsb)
                return stance.AiMoveSpeedSprint;

            // In combat, use running speed
            if (IsInBattle)
                return Math.Min(stance.AiMoveSpeedRun, stance.MaxSpeed);

            // Not in combat (should be roaming), use walk speed
            return Math.Min(stance.AiMoveSpeedWalk, stance.MaxSpeed);
        }
    }

    private GameStanceType _currentGameStance = GameStanceType.Combat;
    public GameStanceType CurrentGameStance
    {
        get => _currentGameStance;
        set
        {
            if (_currentGameStance == value)
                return;

            if (CanFly)
            {
                _currentGameStance = GameStanceType.Fly;
                return;
            }

            if (IsUnderWater)
            {
                if (value == GameStanceType.Combat)
                    _currentGameStance = GameStanceType.CoSwim;
                else
                    _currentGameStance = GameStanceType.Swim;
                return;
            }

            _currentGameStance = value;
        }
    }

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

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        base.DoDie(killer, killReason);
        AggroTable.Clear();
        if (killer is Character character)
        {
            character.AddExp(KillExp, true);
            var mate = MateManager.Instance.GetActiveMate(character.ObjId);
            if (mate != null)
            {
                mate.AddExp(KillExp);
                character.SendMessage($"Pet gained {KillExp} XP");
            }
            //character.Quests.OnKill(this);
            // инициируем событие
            //Task.Run(() => QuestManager.Instance.DoOnMonsterHuntEvents(character, this));
            QuestManager.Instance.DoOnMonsterHuntEvents(character, this);
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
        //var player = unit as Character; // TODO player.Region становится равным null | player.Region becomes null
        Character player = null;
        if (unit is not Npc and not Units.Mate and not Slave)
        {
            player = (Character)unit;
        }

        //player?.SendMessage(ChatType.System, $"AddUnitAggro {player.Name} + {amount} for {this.ObjId}");

        // check self buff tags
        if (Buffs.CheckBuffTag((uint)TagsEnum.NoFight) || Buffs.CheckBuffTag((uint)TagsEnum.Returning))
        {
            ClearAggroOfUnit(unit);
            return;
        }

        // check target buff tags
        if ((unit.Buffs?.CheckBuffTag((uint)TagsEnum.NoFight) ?? false) || (unit.Buffs?.CheckBuffTag((uint)TagsEnum.Returning) ?? false))
        {
            ClearAggroOfUnit(unit);
            return;
        }

        amount = (int)(amount * (unit.AggroMul / 100.0f));
        amount = (int)(amount * (IncomingAggroMul / 100.0f));

        if (AggroTable.TryGetValue(unit.ObjId, out var aggro))
        {
            aggro.AddAggro(kind, amount);
        }
        else
        {
            aggro = new Aggro(unit);
            aggro.AddAggro(kind, amount);
            if (AggroTable.TryAdd(unit.ObjId, aggro))
            {
                unit.Events.OnHealed += OnAbuserHealed;
                unit.Events.OnDeath += OnAbuserDied;
            }

            // TODO: make this party/raid wide? Take into account pets/slaves?
            // If there is a quest starter attached to this NPC, start it when unit gets added for the first time
            // to the aggro list
            if ((Template.EngageCombatGiveQuestId > 0) && player is not null)
            {
                if (!player.Quests.IsQuestComplete(Template.EngageCombatGiveQuestId) && !player.Quests.HasQuest(Template.EngageCombatGiveQuestId))
                    player.Quests.AddQuest(Template.EngageCombatGiveQuestId);
            }
        }

        if (player == null)
        {
            return;
        }
        //player?.Quests.OnAggro(this);
        // инициируем событие
        //Task.Run(() => QuestManager.Instance.DoOnAggroEvents(player, this));
        QuestManager.Instance.DoOnAggroEvents(player, this);
    }

    public void ClearAggroOfUnit(Unit unit)
    {
        if (unit is null)
            return;

        var player = unit as Character;

        // player?.SendMessage(ChatType.System, $"ClearAggroOfUnit {player.Name} for {this.ObjId}");

        var lastAggroCount = AggroTable.Count;
        if (AggroTable.TryRemove(unit.ObjId, out var value))
        {
            unit.Events.OnHealed -= OnAbuserHealed;
            unit.Events.OnDeath -= OnAbuserDied;
        }
        else
        {
            Logger.Warn("Failed to remove unit[{0}] aggro from NPC[{1}]", unit.ObjId, this.ObjId);
        }

        if (AggroTable.Count != lastAggroCount)
            CheckIfEmptyAggroToReturn(unit);
    }

    private static void CheckIfEmptyAggroToReturn(IBaseUnit unit)
    {
        if (unit is not Npc npc)
            return;

        // If aggro table is empty, and too far from spawn, trigger a return to spawn effect.
        if (!npc.AggroTable.IsEmpty)
            return;

        if (npc.Ai != null)
        {
            var distanceToIdle = MathUtil.CalculateDistance(npc.Ai.IdlePosition.Local.Position, npc.Transform.World.Position, true);
            if (distanceToIdle > 4)
                npc.Ai.GoToReturn();
        }

        npc.IsInBattle = false;
    }

    private void CheckIfEmptyAggroToReturn()
    {
        // If aggro table is empty, and too far from spawn, trigger a return to spawn effect.
        if (AggroTable.IsEmpty)
        {
            if (Ai != null)
            {
                var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition.Local.Position, Ai.Owner.Transform.World.Position, true);
                if (distanceToIdle > 4)
                    Ai.GoToReturn();
            }

            IsInBattle = false;
        }
    }

    public void ClearAllAggro()
    {
        foreach (var table in AggroTable)
        {
            var unit = WorldManager.Instance.GetUnit(table.Key);
            if (unit != null)
            {
                unit.Events.OnHealed -= OnAbuserHealed;
                unit.Events.OnDeath -= OnAbuserDied;
            }
        }

        var lastAggroCount = AggroTable.Count;
        AggroTable.Clear();
        if (lastAggroCount > 0)
            CheckIfEmptyAggroToReturn();
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

        /*
        var topAbuser = AggroTable.GetTopTotalAggroAbuserObjId();
        if ((CurrentTarget?.ObjId ?? 0) != topAbuser)
        {
            CurrentAggroTarget = topAbuser; 
            var unit = WorldManager.Instance.GetUnit(topAbuser);
            SetTarget(unit);
            Ai?.OnAggroTargetChanged();
        }
        */
    }

    public void MoveTowards(Vector3 other, float distance, byte flags = 4)
    {
        distance *= Ai.Owner.MoveSpeedMul; // Apply speed modifier
        if (distance < 0.01f)
            return;

        if (Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || Ai.Owner.IsDead)
        {
            //Logger.Debug($"{ObjId} @NPC_NAME({TemplateId}); is stuck in place");
            return;
        }

        if (Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return;
        }

        if ((ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return;

        var oldPosition = Transform.Local.ClonePosition();

        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
        if (targetDist <= 1f)
            return;

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        var travelDist = Math.Min(targetDist, distance);

        // TODO: Implement proper use for Transform.World.AddDistanceToFront
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);
        Transform.Local.SetPosition(newX, newY, newZ);

        // TODO: Implement Transform.World to do proper movement
        if (!CanFly)
        {
            // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
            var updZ = WorldManager.Instance.GetHeight(Transform.ZoneId, newX, newY);
            if (Math.Abs(newZ - updZ) < 1f)
            {
                Transform.Local.SetHeight(updZ);
            }
        }

        var angle = MathUtil.CalculateAngleFrom(Transform.Local.Position, other);
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
        Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Transform.Local.ToRollPitchYawSBytesMovement();

        moveType.X = Transform.Local.Position.X;
        moveType.Y = Transform.Local.Position.Y;
        moveType.Z = Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        //moveType.VelZ = (short)velZ;
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
        moveType.VelX = 0;
        moveType.VelY = 0;
        moveType.VelZ = 0;
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
        moveType.VelX = 0;
        moveType.VelY = 0;
        moveType.VelZ = 0;
        moveType.RotationX = 0;
        moveType.RotationY = 0;
        moveType.RotationZ = Transform.Local.ToRollPitchYawSBytesMovement().Item3;
        moveType.Flags = 4;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = (sbyte)(CurrentAggroTarget > 0 ? 0 : 1);    // COMBAT = 0x0, IDLE = 0x1
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
        Ai.AlreadyTargetted = other != null;
    }

    public void FindPath(Unit abuser)
    {
        Ai.PathNode.pos1 = new Point(Ai.Owner.Transform.World.Position.X, Ai.Owner.Transform.World.Position.Y, Ai.Owner.Transform.World.Position.Z);
        Ai.PathNode.pos2 = new Point(abuser.Transform.World.Position.X, abuser.Transform.World.Position.Y, abuser.Transform.World.Position.Z);

        Ai.PathNode.ZoneKey = Ai.Owner.Transform.ZoneId;
        Ai.PathNode.findPath = Ai.PathNode.FindPath(Ai.PathNode.pos1, Ai.PathNode.pos2);

        Logger.Trace($"AStar: points found Total: {Ai.PathNode.findPath?.Count ?? 0}");
        if (Ai.PathNode.findPath != null)
        {
            for (var i = 0; i < Ai.PathNode.findPath.Count; i++)
            {
                Logger.Trace($"AStar: point {i} coordinates X:{Ai.PathNode.findPath[i].X}, Y:{Ai.PathNode.findPath[i].Y}, Z:{Ai.PathNode.findPath[i].Z}");
            }
        }
    }

    /// <summary>
    /// Find the nearest point
    /// </summary>
    /// <param name="unit"></param>
    /// <returns>return coordinates and change the index of the current point</returns>
    public Vector3 GetClosestPoint(BaseUnit unit)
    {
        // TODO взять точку к которой движемся
        if (Ai.PathNode.findPath == null)
            return Vector3.Zero;

        var pos = new Point(unit.Transform.World.Position.X, unit.Transform.World.Position.Y, unit.Transform.World.Position.Z);
        Ai.PathNode.Current = AiGeoDataManager.FindClosestIndexPoint(Ai.PathNode.findPath, pos);

        return new Vector3(Ai.PathNode.findPath[(int)Ai.PathNode.Current].X, Ai.PathNode.findPath[(int)Ai.PathNode.Current].Y, Ai.PathNode.findPath[(int)Ai.PathNode.Current].Z);
    }

    public void DoDespawn(Npc npc)
    {
        Spawner.DoDespawn(npc);
    }

    /// <summary>
    /// Returns the ranking in this Npc's aggro table in percent
    /// </summary>
    /// <param name="objId"></param>
    /// <returns>Position in the aggro table ranking in percent, 0 = most aggro, 100 = no aggro</returns>
    /// <exception cref="NotImplementedException"></exception>
    public float GetAggroRatingInPercent(uint objId)
    {
        // grab a sorted copy of the aggro list
        var sortedAggro = AggroTable.OrderBy(x => x.Value.TotalAggro).ToList();

        // Find our position in the list
        var pos = 0;
        for (; pos < sortedAggro.Count; pos++)
        {
            if (sortedAggro[pos].Key == objId)
                break;
        }

        // If at the end of the list (not found), don't round anything, always return 100
        if (pos >= sortedAggro.Count)
            return 100f;

        // Return the position in the list 0 = most aggro, 100 = least aggro
        return 1f / sortedAggro.Count * pos;
    }
}

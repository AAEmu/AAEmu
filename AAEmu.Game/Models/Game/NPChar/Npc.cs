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

public partial class Npc : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Npc;
    public override BaseUnitType BaseUnitType => BaseUnitType.Npc;
    public override ModelPostureType ModelPostureType { get => AnimActionId > 0 ? ModelPostureType.ActorModelState : ModelPostureType.None; }

    //public uint TemplateId { get; set; } // moved to BaseUnit
    public NpcTemplate Template { get; set; }
    //public Item[] Equip { get; set; }
    public NpcSpawner Spawner { get; set; }
    public Gimmick Gimmick { get; set; }

    public override UnitCustomModelParams ModelParams => Template.ModelParams;

    /// <summary>
    /// This is the "Idle Animation Id" that is used in UnitModelChangePosture, it can change depending on the time of the day
    /// </summary>
    public uint AnimActionId
    {
        get
        {
            switch (Template.NpcPostureSets.Count)
            {
                // If no postures, just return 0
                case 0:
                    return 0;
                // If only one, always return that one
                case 1:
                    return Template.NpcPostureSets.FirstOrDefault()?.AnimActionId ?? 0;
                default:
                    {
                        // If more than one, we need to grab the Time of Day first
                        var myTime = TimeManager.Instance.GetTime;
                        return Template.NpcPostureSets.FirstOrDefault(x => x.StartTodTime <= myTime)?.AnimActionId ?? 0;
                    }
            }
        }
    }

    public override float Scale => Template.Scale;

    public override byte RaceGender => (byte)(16 * Template.Gender + Template.Race);

    public NpcAi Ai { get; set; } // New framework
    public ConcurrentDictionary<uint, Aggro> AggroTable { get; }

    public BaseUnit CurrentAggroTarget
    {
        get => _currentAggroTarget;
        set
        {
            if (_currentAggroTarget == value)
                return;

            if (value != null)
                SendPacketToPlayers([value], new SCAggroTargetChangedPacket(ObjId, value.ObjId));
            // BroadcastPacket(new SCAggroTargetChangedPacket(ObjId, value.ObjId), false);

            _currentAggroTarget = value;
        }
    }

    public bool CanFly { get; set; } // TODO mark Npc's that can fly so that they don't land on the ground when calculating the Z height
    //Tagging works differently to Aggro.:
    public Tagging CharacterTagging { get; set; }


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
    private BaseUnit _currentAggroTarget;

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
    public MoveTypeAlertness CurrentAlertness { get; set; }

    #region Attributes
    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
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
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["level"] = Level;
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            //parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["level"] = Level;
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            //parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["ab_level"] = 0;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.MeleeDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.RangedDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            //parameters["str"] = Str;
            parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Npc, UnitFormulaKind.SpellDpsInc);
            var parameters = new Dictionary<string, double>();
            parameters["level"] = Level;
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
            //parameters["str"] = Str;
            //parameters["dex"] = Dex;
            //parameters["sta"] = Sta;
            //parameters["int"] = Int;
            //parameters["spi"] = Spi;
            //parameters["fai"] = Fai;
            //parameters["npc_template"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId);
            //parameters["npc_kind"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId);
            parameters["npc_grade"] = FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId);
            parameters["heir_level"] = Template.HeirLevel;
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
        CharacterTagging = new Tagging(this);//Adding because Tagging works differently than Aggro
        //Equip = new Item[28];
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        var eligiblePlayers = new HashSet<Character>();
        if (CharacterTagging.TagTeam != 0)
        {
            //A team has tagging rights
            var team = TeamManager.Instance.GetActiveTeam(CharacterTagging.TagTeam);
            if (team != null)
            {

                //Just to check the team is still a valid team.
                foreach (var member in team.Members)
                {
                    if (member != null && member.Character != null)
                    {
                        if (member.Character is Character tm)
                        {
                            var distance = tm.Transform.World.Position - this.Transform.World.Position;
                            if (distance.Length() <= 200)
                            {
                                eligiblePlayers.Add(tm);
                            }
                        }
                    }
                }
            }
            else if (CharacterTagging.Tagger != null)
            {
                //A player has tag rights, but the team is not valid.
                eligiblePlayers.Add(CharacterTagging.Tagger);
            }
        }
        else if (CharacterTagging.Tagger != null)
        {
            //A player has tag rights
            eligiblePlayers.Add(CharacterTagging.Tagger);
        }

        // Logger.Warn($"Eligible killers count is {eligiblePlayers.Count }");

        if (eligiblePlayers.Count == 0 && killer is Character characterKiller)
        {
            QuestManager.Instance.DoOnMonsterHuntEvents(characterKiller, this);//No eligible owner, but the killer is a character.
            characterKiller.AddExp(KillExp, true);
            var mates = MateManager.Instance.GetActiveMates(characterKiller.ObjId); // в версии 3+ может быть несколько
            if (mates != null)
            {
                foreach (var mate in mates)
                {
                    if (mate == null) continue;
                    mate.AddExp(KillExp);
                    characterKiller.SendMessage($"Pet gained {KillExp} XP");
                }
            }
        }
        else
        {
            var isFullTeam = false;
            var isRaid = false;
            if (CharacterTagging.TagTeam != 0)
            {
                //A team has tagging rights
                var team = TeamManager.Instance.GetActiveTeam(CharacterTagging.TagTeam);
                if (team != null)
                {
                    if (!team.IsParty)
                    {
                        isRaid = true;
                        //Team is a raid.
                    }
                    else if (team.MembersCount() > 3)
                    {
                        isFullTeam = true;
                    }
                }
            }

            foreach (var pl in eligiblePlayers)
            {
                var plKillXP = 0;
                var mateKillXP = 0;
                var plMod = 1f;
                var mateMod = 1f;

                if (isRaid)
                {
                    //Player is in a raid. 1.2, pet XP is capped a full team value, but player gets raid XP regardless of how many raiders are present.
                    plMod = 0.33f;
                    mateMod = 0.66f;
                }
                else if (isFullTeam)
                {
                    //Player is in a team of more than 3 people. Player gets full party XP regardless of how many party members are present.
                    plMod = 0.66f;
                    mateMod = 0.66f;
                }

                else if (eligiblePlayers.Count > 1 && eligiblePlayers.Count <= 3)
                {
                    //If players are between 2 and 3, we scale. At this point, the party doesn't matter, just nearby players. 
                    if (eligiblePlayers.Count == 2)
                    {
                        plMod = 0.90f;
                        mateMod = 0.90f;
                    }
                    else if (eligiblePlayers.Count == 3)
                    {
                        plMod = 0.875f;
                        mateMod = 0.875f;
                    }
                }
                else
                {
                    //Player is solo, or at least only 1 player is close enough to get rights
                    plMod = 1f;
                    mateMod = 1f;
                }

                //Now we need to scale XP based on level difference, which gets a bit more complex.


                if (pl.Level >= this.Level + 10 || pl.Level <= this.Level - 10)
                {
                    //No XP for you or your pet. Will check on the +10
                }
                else
                {
                    var LevDif = 1.0f;
                    var levelDifference = pl.Level - this.Level;

                    if (levelDifference > 0)
                    {
                        // pl.Level is above this.Level
                        LevDif = 1.0f - (0.1f * levelDifference);
                    }
                    else if (levelDifference < 0)
                    {
                        // pl.Level is below this.Level
                        LevDif = 1.0f + (0.1f * -levelDifference);
                    }

                    plKillXP = (int)((KillExp * plMod) * LevDif);
                    mateKillXP = (int)((KillExp * mateMod) * LevDif);

                    pl.AddExp(plKillXP, true);
                    var mates = MateManager.Instance.GetActiveMates(pl.ObjId); // в версии 3+ может быть несколько
                    if (mates != null)
                    {
                        foreach (var mate in mates)
                        {
                            if (mate == null) continue;
                            mate.AddExp(mateKillXP);
                            pl.SendMessage($"Pet gained {mateKillXP} XP");
                        }
                    }
                }
                //character.Quests.OnKill(this);
                // инициируем событие
                //Task.Run(() => QuestManager.Instance.DoOnMonsterHuntEvents(character, this));
                QuestManager.Instance.DoOnMonsterHuntEvents(pl, this);
            }
        }
        base.DoDie(killer, killReason);
        ClearAllAggroTargetsAndCheckCombatState();
        // AggroTable.Clear();
        CharacterTagging.ClearAllTaggers();
        CurrentAggroTarget = null;

        Spawner?.DecreaseCount(this);
        Ai?.GoToDead();
    }

    private void ClearAllAggroTargetsAndCheckCombatState()
    {
        List<Character> playerAggroList = new();
        // Generate a list of all player that we had aggro on
        foreach (var (objId, aggro) in AggroTable)
        {
            var unit = WorldManager.Instance.GetGameObject(objId);
            if (unit is Character player)
                playerAggroList.Add(player);
        }
        // Clear the aggro table
        AggroTable.Clear();

        // Check if those target players still have aggro on something else, if not, clear their combat timers
        foreach (var player in playerAggroList)
        {
            ClearAggroOfUnit(player);
            if (player.IsInAggroListOf.Count <= 0)
            {
                // Cancel combat
                player.IsInBattle = false;
            }
        }
    }

    public override void AddVisibleObject(Character character)
    {
        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc));

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
        var player = unit as Character;
        // Character player = null;
        // if (unit is not Npc and not Units.Mate and not Slave)
        // {
        //     player = (Character)unit;
        // }
        // player?.SendMessage(ChatType.System, $"AddUnitAggro {player.Name} + {amount} for {this.ObjId}");

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

        //Add Tagging if it was damage aggro
        if (kind == AggroKind.Damage)
            CharacterTagging.AddTagger(unit, amount);


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

            // Send initial hit packet as well
            unit.SendPacketToPlayers([this, unit], new SCCombatFirstHitPacket(this.ObjId, unit.ObjId, 0));
        }

        if (player == null)
            return;

        if (aggro.TotalAggro > 0 && !IsDead && Hp > 0 && !player.IsInAggroListOf.ContainsKey(this.ObjId))
        {
            player.IsInAggroListOf.Add(this.ObjId, this);
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
        if (player != null && player.IsInAggroListOf.ContainsKey(ObjId))
        {
            player.IsInAggroListOf.Remove(ObjId);
        }

        // var player = unit as Character;
        // player?.SendMessage($"ClearAggroOfUnit {player.Name} for {this.ObjId}");

        var lastAggroCount = AggroTable.Count;
        if (lastAggroCount <= 0)
        {
            return;
        }
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

    //Tagging!


    private static void CheckIfEmptyAggroToReturn(IBaseUnit unit)
    {
        if (unit is not Npc npc)
            return;

        // If aggro table is empty, and too far from spawn, trigger a return to spawn effect.
        if (!npc.AggroTable.IsEmpty)
            return;

        if (npc.Ai != null)
        {
            var distanceToIdle = MathUtil.CalculateDistance(npc.Ai.IdlePosition, npc.Transform.World.Position, true);
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
                var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, Ai.Owner.Transform.World.Position, true);
                if (distanceToIdle > 4)
                    Ai.GoToReturn();
            }

            IsInBattle = false;
        }
    }

    public void ClearAllAggro()
    {
        ///Adding for tagging
        CharacterTagging.ClearAllTaggers();

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
        ClearAllAggroTargetsAndCheckCombatState();
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

    public void MoveTowards(Vector3 other, float distance, byte actorFlags = 4)
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
            if (updZ != 0 && Math.Abs(newZ - updZ) < 1f)
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
        moveType.ActorFlags = actorFlags;     // 5-walk, 4-run, 3-stand still
        moveType.Flags = MoveTypeFlags.Moving | (IsInBattle ? MoveTypeFlags.InCombat : 0); // MoveTypeFlags.Stopping;

        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 127;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = CurrentGameStance;    // COMBAT = 0x0, IDLE = 0x1
        moveType.Alertness = CurrentAlertness;
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
        moveType.Flags = MoveTypeFlags.Moving | (IsInBattle ? MoveTypeFlags.InCombat : 0); ; // 4;

        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
        moveType.Alertness = CurrentAlertness;
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
        moveType.Flags = MoveTypeFlags.Stopping | (IsInBattle ? MoveTypeFlags.InCombat : 0); // 4;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = CurrentGameStance;// (sbyte)(CurrentAggroTarget?.ObjId > 0 ? 0 : 1);    // COMBAT = 0x0, IDLE = 0x1
        moveType.Alertness = CurrentAlertness;
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
        BroadcastPacket(new SCTargetChangedPacket(ObjId, other?.ObjId ?? 0), true);
        Ai.AlreadyTargeted = other != null;
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

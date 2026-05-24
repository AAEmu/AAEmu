using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.NPChar;

public partial class Npc : Unit
{
    public override UnitTypeFlag TypeFlag => UnitTypeFlag.Npc;
    public override BaseUnitType BaseUnitType => BaseUnitType.Npc;
    public override ModelPostureType ModelPostureType { get => AnimActionId > 0 ? ModelPostureType.ActorModelState : ModelPostureType.None; }

    //public uint TemplateId { get; set; } // moved to BaseUnit
    public NpcTemplate Template { get; set; }
    //public Item[] Equip { get; set; }
    public NpcSpawner Spawner { get; set; }

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

    public bool CanFly { get; set; } // TODO: mark NPCs that can fly so that they don't land on the ground when calculating the Z height

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
            if (Ai?.GetCurrentBehavior() is ReturnStateBehavior _)
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
                _currentGameStance = value == GameStanceType.Combat ? GameStanceType.CoSwim : GameStanceType.Swim;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["ab_level"] = 0,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.RangedDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["npc_template"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)Template.NpcTemplateId),
                ["npc_kind"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcKind, (byte)Template.NpcKindId),
                ["npc_grade"] =
                FormulaManager.Instance.GetUnitVariable(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)Template.NpcGradeId)
            };
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

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        var eligiblePlayers = new HashSet<Character>();
        if (CharacterTagging.TagTeam != 0)
        {
            // A team has tagging rights
            var team = TeamManager.Instance.GetActiveTeam(CharacterTagging.TagTeam);
            if (team != null)
            {
                // Just to check the team is still a valid team.
                foreach (var member in team.Members)
                {
                    if (member?.Character != null)
                    {
                        if (member.Character.GetDistanceTo(this, true) <= Items.Containers.LootingContainer.MaxLootingRange)
                        {
                            eligiblePlayers.Add(member.Character);
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
            QuestManager.Instance.DoOnMonsterHuntEvents(characterKiller, this); // No eligible owner, but the killer is a character.
            characterKiller.AddExp(KillExp, true);
            var mateList = characterKiller.ParentWorld.MateManager.GetActiveMates(characterKiller.Id);
            foreach (var mate in mateList)
            {
                mate.AddExp(KillExp);
                // TODO: Proper message?
                characterKiller.SendMessage($"Pet gained {KillExp} XP");
            }
        }
        else
        {
            var isFullTeam = false;
            var isRaid = false;
            if (CharacterTagging.TagTeam != 0)
            {
                // A team has tagging rights
                var team = TeamManager.Instance.GetActiveTeam(CharacterTagging.TagTeam);
                if (team != null)
                {
                    if (!team.IsParty)
                    {
                        isRaid = true;
                        // Team is a raid.
                    }
                    else if (team.MembersCount() > 3)
                    {
                        isFullTeam = true;
                    }
                }
            }

            foreach (var pl in eligiblePlayers)
            {
                var plMod = 1f;
                var mateMod = 1f;

                if (isRaid)
                {
                    // Player is in a raid. 1.2, pet XP is capped a full team value, but player gets raid XP regardless of how many raiders are present.
                    plMod = 0.33f;
                    mateMod = 0.66f;
                }
                else if (isFullTeam)
                {
                    // Player is in a team of more than 3 people. Player gets full party XP regardless of how many party members are present.
                    plMod = 0.66f;
                    mateMod = 0.66f;
                }

                else if (eligiblePlayers.Count is > 1 and <= 3)
                {
                    // If players are between 2 and 3, we scale. At this point, the party doesn't matter, just nearby players. 
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
                    // Player is solo, or at least only 1 player is close enough to get rights
                    plMod = 1f;
                    mateMod = 1f;
                }

                // Now we need to scale XP based on level difference, which gets a bit more complex.

                if (pl.Level >= this.Level + 10 || pl.Level <= this.Level - 10)
                {
                    // No XP for you or your pet. Will check on the +10
                }
                else
                {
                    var levDif = 1.0f;
                    var levelDifference = pl.Level - this.Level;

                    if (levelDifference > 0)
                    {
                        // pl.Level is above this.Level
                        levDif = 1.0f - 0.1f * levelDifference;
                    }
                    else if (levelDifference < 0)
                    {
                        // pl.Level is below this.Level
                        levDif = 1.0f + 0.1f * -levelDifference;
                    }

                    var plKillXp = (int)(KillExp * plMod * levDif);
                    var mateKillXp = (int)(KillExp * mateMod * levDif);

                    pl.AddExp(plKillXp, true);
                    var mateList = pl.ParentWorld.MateManager.GetActiveMates(pl.Id);
                    foreach (var mate in mateList)
                    {
                        mate.AddExp(mateKillXp);
                        // TODO: Proper message?
                        pl.SendMessage($"Pet gained {mateKillXp} XP");
                    }
                }

                // character.Quests.OnKill(this);
                // инициируем событие
                // Task.Run(() => QuestManager.Instance.DoOnMonsterHuntEvents(character, this));
                QuestManager.Instance.DoOnMonsterHuntEvents(pl, this);
            }
        }
        base.DoDie(killer, killReason);
        ClearAllAggroTargetsAndCheckCombatState();
        // AggroTable.Clear();
        CharacterTagging.ClearAllTaggers();
        CurrentAggroTarget = null;

        Spawner?.DoDespawn(this);
        Ai?.GoToDead();
    }

    private void ClearAllAggroTargetsAndCheckCombatState()
    {
        List<Character> playerAggroList = [];
        // Generate a list of all player that we had aggro on
        foreach (var (objId, aggro) in AggroTable)
        {
            var unit = aggro.Owner.ParentWorld.GetGameObject(objId);
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
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));

        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    //Tagging!

    public void CheckIfEmptyAggroToReturn(IBaseUnit unit)
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

    public override void ClearAllAggro()
    {
        base.ClearAllAggro();

        var lastAggroCount = AggroTable.Count;
        ClearAllAggroTargetsAndCheckCombatState();
        if (lastAggroCount > 0)
            CheckIfEmptyAggroToReturn();
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

    /// <summary>
    /// Moves towards the target position
    /// </summary>
    /// <param name="other">Target position</param>
    /// <param name="distance">Maximum distance to move (before multipliers)</param>
    /// <param name="actorFlags">ActorFlags to use for the movement packet</param>
    /// <param name="rangeTolerance">Makes the function return true if target distance is less than or equil to this value</param>
    /// <returns>True if withing rangeTolerance of other</returns>
    public bool MoveTowards(Vector3 other, float distance, byte actorFlags = 4, float rangeTolerance = 1f)
    {
        if ((ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return false;

        if (DisplacedUntil.HasValue && DisplacedUntil.Value > DateTime.UtcNow)
            return false;

        distance *= Ai.Owner.MoveSpeedMul; // Apply speed modifier
        if (distance < 0.01f)
            return false;

        if (Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || Ai.Owner.IsDead)
        {
            //Logger.Debug($"{ObjId} @NPC_NAME({TemplateId}); is stuck in place");
            return false;
        }

        // Shackle (160) is the broad "root family" tag — most snares ride it.
        // The exclude list strips buffs that ALSO have the DecreaseMoveSpeed
        // (161) tag, which is how slow debuffs like Charged Bolt are encoded:
        // they ship as Shackle + DecreaseMoveSpeed, mean "slow, not stop", and
        // without this exclusion the NPC would be fully rooted by every slow it
        // gets hit by. The dedicated Snare (27) check is kept alongside to
        // catch any Snare-only-tagged buffs that don't ride the Shackle family —
        // matches the dual-check pattern in DashSkillController and
        // LeapSkillController so all three gates agree.
        if (Ai.Owner.Buffs.CheckBuffsExcludingTags(
                SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle),
                [(uint)SkillConstants.DecreaseMoveSpeed]) ||
            Ai.Owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return false;
        }

        var oldPosition = Transform.Local.ClonePosition();

        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
        if (targetDist <= rangeTolerance)
            return true;

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        var travelDist = Math.Min(targetDist, distance);

        // TODO: Implement proper use for Transform.World.AddDistanceToFront
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);
        var targetPositionZ = WorldManager.Instance.GetReferenceHeight(Ai, newX, newY, newZ, Transform.ZoneId);
        Transform.Local.SetPosition(newX, newY, targetPositionZ);

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
        return false;
    }

    public void LookTowards(Vector3 other, byte flags = 4)
    {
        //var oldPosition = Transform.Local.ClonePosition();
        //oldPosition.Z = WorldManager.Instance.GetReferenceHeight(Ai, oldPosition.X, oldPosition.Y, oldPosition.Z, Transform.ZoneId);
        //Transform.Local.SetPosition(oldPosition);

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
        moveType.Flags = MoveTypeFlags.Moving | (IsInBattle ? MoveTypeFlags.InCombat : 0); // 4;

        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
        moveType.Alertness = CurrentAlertness;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        //CheckMovedPosition(oldPosition);
        //SetPosition(Position);
        BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), false);
    }

    public void StopMovement()
    {
        //var oldPosition = Transform.Local.ClonePosition();
        //oldPosition.Z = WorldManager.Instance.GetReferenceHeight(Ai, oldPosition.X, oldPosition.Y, oldPosition.Z, Transform.ZoneId);
        //Transform.Local.SetPosition(oldPosition);

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
        Ai.PathNode.StartPointPos = new Vector3(Ai.Owner.Transform.World.Position.X, Ai.Owner.Transform.World.Position.Y, Ai.Owner.Transform.World.Position.Z);
        Ai.PathNode.EndPointPos = new Vector3(abuser.Transform.World.Position.X, abuser.Transform.World.Position.Y, abuser.Transform.World.Position.Z);

        Ai.PathNode.ZoneKey = Ai.Owner.Transform.ZoneId;
        var resList = Ai.PathNode.FindPath(Ai.Owner.ParentWorld, Ai.PathNode.StartPointPos, Ai.PathNode.EndPointPos);
        resList.Add(abuser.Transform.World.Position);
        var reducedPath = ParentWorld.Template.GeoData.ReducePath(resList, 10);
        Ai.PathNode.FoundPath = reducedPath;
        if (abuser is Character player)
        {
            player.SendMessage($"Aggro from {Ai.Owner.ObjId}, getting attack path in {Ai.PathNode.FoundPath.Count}/{resList.Count} steps");
            foreach (var v3 in Ai.PathNode.FoundPath)
            {
                player.SendMessage($"Path step -> {v3}");
            }
        }
    }

    /// <summary>
    /// Runs parent spawner's DoDeSpawn
    /// </summary>
    /// <param name="npc"></param>
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

    /// <summary>
    /// Add all spawn buffs that should be applied when the Npc gets created
    /// </summary>
    public virtual void InitializeSpawnBuffs()
    {
        // Initial Buffs
        foreach (var buffId in Template.Buffs)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buff == null)
            {
                Logger.Warn($"BuffId {buffId} for npc {TemplateId} not found");
                continue;
            }

            var obj = new SkillCasterUnit(ObjId);
            buff.Apply(this, obj, this, null, null, new EffectSource(), null, DateTime.UtcNow);
        }

        // Passive Buffs
        foreach (var npcPassiveBuff in Template.PassiveBuffs)
        {
            var passive = new PassiveBuff { Template = npcPassiveBuff.PassiveBuff };
            passive.Apply(this);
        }

        // Stat bonus effects
        foreach (var bonusTemplate in Template.Bonuses)
        {
            var bonus = new Bonus
            {
                Template = bonusTemplate,
                Value = bonusTemplate.Value // TODO using LinearLevelBonus
            };
            AddBonus(0, bonus);
        }
    }

    public override void Delete()
    {
        // Detach AI
        if (Ai != null)
        {
            Ai.ShouldTick = false;
            Ai.Owner = null;
            Ai = null;
        }

        base.Delete();
    }

    public override Character GetOwnerCharacter()
    {
        // Not sure if this needs to be implemented for escort NPCs
        // if (OwnerId > 0)
        //     return WorldManager.Instance.GetCharacterById(OwnerId)?.GetOwnerCharacter();
        return null;
    }
}

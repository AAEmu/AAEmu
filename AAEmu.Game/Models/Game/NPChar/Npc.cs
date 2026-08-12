using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Static;
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
    /// Created by WorldIntegration.MirrorZoneNpcSpawn — zone owns AI; Game invents thin SCUnitState.
    /// Jul 18: full Face+equip UnitState on visibility worked (Nuian 警备兵). Later Soft gates/Skin rewrite broke it.
    /// </summary>
    public bool IsZoneMirror { get; set; }

    /// <summary>
    /// A zone mirror whose corpse timer has already sent WZNpcStartDespawn. The zone owns the
    /// teardown from there and answers with ZWRemoveNpc; this only marks that we are waiting, so
    /// the deadline can fall back to a forced removal instead of leaking the mirror.
    /// </summary>
    public bool ZoneDespawnSignaled { get; set; }

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

    public BaseUnit CurrentAggroTarget
    {
        get => _currentAggroTarget;
        set
        {
            if (_currentAggroTarget == value)
                return;

            if (value != null)
                SendPacketToPlayers([value], new SCTargetChangedPacket(ObjId, value.ObjId));

            _currentAggroTarget = value;
        }
    }

    /// <summary>Set from the actor model (fly_mode or MovementId 2): keeps the spawner's Z instead of terrain height.</summary>
    public bool CanFly { get; set; }

    /// <summary>
    /// The <c>flag</c> byte of the NPC id-type block in SCUnitState (0x097).
    /// destination name. Template 5476 is treated as bit 1 regardless of this byte.
    /// </summary>
    public virtual byte UnitStateFlag => 0;

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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
                    res += (int)bonus.Value;
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
                ["heir_level"] = 0, // NPCs have no heir level
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
        // Zone mirrors have no Spawner — World schedules corpse cleanup, but Zone owns respawn.
        // Tell Zone the unit died (WZUnitDeath) so it enters corpse state; on timeout World sends
        // WZNpcStartDespawn so Zone GO_TO_DESPAWN → ZWRemoveNpc → NpcSpawner respawn.
        if (Spawner == null && IsZoneMirror && ParentWorld?.SpawnManager != null)
        {
            WorldIntegration.RelayUnitDeathToZone?.Invoke(ObjId);
            // Unlooted corpses: ~20s base + loot hold (match LootingContainer extension).
            // Zone also times out; World cleanup must notify Zone or liveCount never drops.
            var delay = TimeSpan.FromSeconds(20);
            if (LootingContainer != null && LootingContainer.Items.Count > 0)
                delay += TimeSpan.FromSeconds(LootingContainer.LootDespawnExtensionTime);
            Despawn = DateTime.UtcNow.Add(delay);
            ParentWorld.SpawnManager.AddDespawn(this);
        }
    }

    private void ClearAllAggroTargetsAndCheckCombatState()
    {
        // Snapshot before the base clear removes the entries. Clearing the dictionary first made
        // ClearAggroOfUnit return early and left Character.IsInAggroListOf permanently stale.
        var playerAggroList = AggroTable.Values
            .Select(aggro => aggro.Owner)
            .OfType<Character>()
            .Distinct()
            .ToArray();

        base.ClearAllAggro();

        // Check if those target players still have aggro on something else, if not, clear their combat timers
        foreach (var player in playerAggroList)
        {
            if (player.IsInAggroListOf.Count <= 0)
            {
                // Cancel combat
                player.IsInBattle = false;
            }
        }
    }

    /// <summary>
    /// AAEMU_DISABLE_MIRROR_NPC=1 | AAEMU_MIRROR_NPC_MAX=N (default 50; 0=unlimited) |
    /// AAEMU_MIRROR_NPC_BURST=N (0=flush all/tick) | AAEMU_MIRROR_NPC_INTERVAL_MS (0=off) |
    /// AAEMU_MIRROR_NPC_AOI=metres | AAEMU_MIRROR_NPC_GRACE_MS
    /// </summary>
    public static readonly bool DisableMirrorNpcStreaming =
        System.Environment.GetEnvironmentVariable("AAEMU_DISABLE_MIRROR_NPC") == "1";

    public static readonly int MirrorNpcMaxPerCharacter = ParseMirrorNpcMax();
    public static readonly int MirrorNpcImmediateBurst = ParseMirrorNpcBurst();

    /// <summary>Squared soft interest radius for mirror SC (commercial AOI, not sticky region set).</summary>
    public static readonly float MirrorNpcAoiRadiusSq = ParseMirrorNpcAoiRadiusSq();

    /// <summary>
    /// Event/tower rifts fly and land far from the player’s ambient 100 m stream radius — they still
    /// need SCUnitState so ZW→SC movement (fly-in) is visible on the map. Default 1.5 km; override
    /// <c>AAEMU_MIRROR_PRIORITY_AOI</c> (metres).
    /// </summary>
    public static readonly float MirrorStreamPriorityAoiRadiusSq = ParseMirrorStreamPriorityAoiRadiusSq();

    /// <summary>
    /// Event NPCs from loaded tower-def relationships that must paint even when ambient mirrors
    /// filled MAX first. Returns false until <see cref="TowerDefGameData"/> has loaded.
    /// </summary>
    public bool IsMirrorStreamPriority =>
        IsZoneMirror && TowerDefGameData.Instance.IsTowerDefEventNpc(TemplateId);

    private static int ParseMirrorNpcMax()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_MAX");
        if (string.IsNullOrEmpty(raw))
            return 50;
        return int.TryParse(raw, out var n) && n >= 0 ? n : 50;
    }

    private static int ParseMirrorNpcBurst()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_BURST");
        if (string.IsNullOrEmpty(raw))
            return 0;
        return int.TryParse(raw, out var n) && n >= 0 ? n : 0;
    }

    private static float ParseMirrorNpcAoiRadiusSq()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_NPC_AOI");
        var metres = 100f;
        if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 20f)
            metres = n;
        return metres * metres;
    }

    private static float ParseMirrorStreamPriorityAoiRadiusSq()
    {
        var raw = System.Environment.GetEnvironmentVariable("AAEMU_MIRROR_PRIORITY_AOI");
        var metres = 1500f;
        if (float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 50f)
            metres = n;
        return metres * metres;
    }

    /// <summary>AOI for this mirror’s soft stream (priority event NPCs use a larger radius).</summary>
    public float MirrorStreamAoiRadiusSq =>
        IsMirrorStreamPriority ? MirrorStreamPriorityAoiRadiusSq : MirrorNpcAoiRadiusSq;

    public override void AddVisibleObject(Character character)
    {
        if (DisableMirrorNpcStreaming && IsZoneMirror && WorldIntegration.ZoneAuthority)
        {
            base.AddVisibleObject(character);
            return;
        }

        if (IsZoneMirror && WorldIntegration.ZoneAuthority)
        {
            // Priority event NPCs steal a MAX slot if packed AOI only has ambient mirrors.
            if (IsMirrorStreamPriority && !character.CanStreamMirrorNow(this))
                character.TryEvictFarthestStreamedForPriority(this);

            // Queue only while loading / outside AOI / at MAX (drain + cull recycle slots).
            if (character.CanStreamMirrorNow(this))
                SendUnitStateTo(character);
            else
                character.EnqueuePendingMirrorSpawn(this);
        }
        else
            SendUnitStateTo(character);

        base.AddVisibleObject(character);
    }

    public void SendUnitStateTo(Character character)
    {
        if (DisableMirrorNpcStreaming && IsZoneMirror && WorldIntegration.ZoneAuthority)
            return;

        if (IsZoneMirror && WorldIntegration.ZoneAuthority)
        {
            if (character.MirrorNpcStatesSentIds.ContainsKey(ObjId))
                return;

            if (MirrorNpcMaxPerCharacter > 0 &&
                character.MirrorNpcStatesSentCount >= MirrorNpcMaxPerCharacter)
            {
                if (!IsMirrorStreamPriority ||
                    !character.TryEvictFarthestStreamedForPriority(this))
                    return;
            }

            if (!character.MirrorNpcStatesSentIds.TryAdd(ObjId, 0))
                return;
        }

        NpcHeightDiagnostics.RecordPaint(
            ObjId, TemplateId, character.Name, Transform.Local.Position.Z);

        character.SendPacket(new SCUnitStatePacket(this));
        // 0xBF. Cosplay is already on the UnitState equipment block for slots 27/31–33.
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));

        // 0x02E (client may fill from local template), but when we do send it the client gate in
        // Fresh NPCs start at faction 0; sending (Faction.Id, Faction.Id) no-ops and leaves them
        // neutral (Zeromus: "faction 0 … same visuals"). Must send old=0 → new=real.
        // AAEMU_DISABLE_NPC_FACTION=1 to mute.
        if (Faction != null &&
            System.Environment.GetEnvironmentVariable("AAEMU_DISABLE_NPC_FACTION") != "1")
            character.SendPacket(new SCUnitFactionChangedPacket(
                ObjId, Name ?? "", FactionsEnum.Invalid, Faction.Id, false));

        // SC UnitState writes empty buff lists for NPCs. OnSpawn FX (hellgate storm, etc.) only
        // arrives via SCBuffCreated. Re-paint when this client got UnitState after World already
        // applied buffs (late AOI / post-cull re-stream) — Broadcast at Apply time never reached them.
        if (IsZoneMirror && WorldIntegration.ZoneAuthority)
            SendActiveBuffsTo(character);

        // Debug only — Info thrash was multi-MB/s during wave packs when something re-painted.
        if (IsMirrorStreamPriority)
            Logger.Debug(
                "Mirror priority SCUnitState → {0} bc={1} tpl={2} zone={3}",
                character.Name, ObjId, TemplateId, Transform?.ZoneId ?? 0);
    }

    /// <summary>
    /// Unicast non-passive buffs currently on this unit (client icons / fx_group). Safe to call on
    /// every UnitState stream; client replaces by index.
    /// </summary>
    public void SendActiveBuffsTo(Character character)
    {
        if (character == null || Buffs == null)
            return;

        var good = new List<Buff>();
        var bad = new List<Buff>();
        var hidden = new List<Buff>();
        Buffs.GetAllBuffs(good, bad, hidden, includeAllPassives: false);

        var sent = 0;
        foreach (var buff in good.Concat(bad).Concat(hidden))
        {
            if (buff == null || buff.Passive || buff.Template == null)
                continue;
            character.SendPacket(new SCBuffCreatedPacket(buff));
            sent++;
        }

        if (sent > 0 && IsMirrorStreamPriority)
            Logger.Info(
                "Mirror priority SCBuffCreated re-sync → {0} bc={1} tpl={2} count={3}",
                character.Name, ObjId, TemplateId, sent);
    }

    public override void RemoveVisibleObject(Character character)
    {
        // Soft interest leave must not despawn tower / event rifts. True despawn goes through
        // Hide/Delete (IsVisible=false) after ZWRemove, which is the only path that should SC-remove them.
        if (IsZoneMirror && WorldIntegration.ZoneAuthority && IsMirrorStreamPriority && IsVisible)
        {
            Logger.Debug(
                "Skip soft SCUnitsRemoved for priority mirror bc={0} tpl={1} char={2}",
                ObjId, TemplateId, character?.Name);
            return;
        }

        if (IsZoneMirror && WorldIntegration.ZoneAuthority)
            character.ReleaseMirrorNpcSlot(ObjId);

        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    //Tagging!

    /// <summary>
    /// Drops the combat flag once nothing is on the aggro table. Leashing back to spawn is the
    /// Zone's call and arrives as ZWClearCombat / the 11503 reset skill.
    /// </summary>
    public void CheckIfEmptyAggroToReturn(IBaseUnit unit)
    {
        if (unit is not Npc npc || !npc.AggroTable.IsEmpty)
            return;

        npc.IsInBattle = false;
    }

    private void CheckIfEmptyAggroToReturn()
    {
        if (AggroTable.IsEmpty)
            IsInBattle = false;
    }

    public override void ClearAllAggro()
    {
        var lastAggroCount = AggroTable.Count;
        ClearAllAggroTargetsAndCheckCombatState();
        if (lastAggroCount > 0)
            CheckIfEmptyAggroToReturn();
    }

    /// <summary>
    /// Records damage on the aggro table for kill credit and loot rights. Target selection and the
    /// response to it belong to the Zone.
    /// </summary>
    public void OnDamageReceived(Unit attacker, int amount)
    {
        AddUnitAggro(AggroKind.Damage, attacker, amount);
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
        distance *= MoveSpeedMul; // Apply speed modifier
        if (distance < 0.01f)
            return false;

        if (Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || IsDead)
        {
            //Logger.Debug($"{ObjId} @NPC_NAME({TemplateId}); is stuck in place");
            return false;
        }

        if (Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Shackle)) ||
            Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)SkillConstants.Snare)))
        {
            return false;
        }

        if ((ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return false;

        var oldPosition = Transform.Local.ClonePosition();

        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
        if (targetDist <= rangeTolerance)
            return true;

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

        var travelDist = Math.Min(targetDist, distance);

        // TODO: Implement proper use for Transform.World.AddDistanceToFront
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(travelDist, targetDist, Transform.Local.Position, other);
        var targetPositionZ = WorldManager.Instance.GetReferenceHeight(this, newX, newY, newZ, Transform.ZoneId);
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

    /// <summary>
    /// Builds a stand-still movement body for this NPC at its current position, stamped with the given
    /// physics time. Used by MirrorMovementStreamTask to keep the client's world clock advancing: the
    /// that actually move, and mirrored NPCs stand idle. Byte-identical to a real idle stand (VelZero,
    /// Stopping, no actor sub-blocks), so the client processes it exactly as it would a commercial one.
    /// Real zone movement (relayed 0x08) supersedes these whenever the unit truly moves.
    /// </summary>
    public UnitMoveType BuildIdleMoveType(uint time)
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
        moveType.Flags = MoveTypeFlags.Stopping | (IsInBattle ? MoveTypeFlags.InCombat : 0);
        moveType.DeltaMovement = new sbyte[3];
        moveType.Stance = CurrentGameStance;
        moveType.Alertness = CurrentAlertness;
        moveType.Time = time;
        return moveType;
    }

    public override void OnSkillEnd(Skill skill)
    {
        // AI?.OnSkillEnd(skill);
    }

    public void SetTarget(Unit other)
    {
        CurrentTarget = other;
        BroadcastPacket(new SCTargetChangedPacket(ObjId, other?.ObjId ?? 0), true);
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

    /// <summary>
    /// Apply BuffEffects from np_skills with OnSpawn onto this unit for client visuals.
    /// Hellgate storm FX (`fx_group_id`) attaches via SCBuffCreated — SC UnitState for Npcs writes
    /// empty buff lists. Call only after the unit is region-visible (has viewers).
    /// </summary>
    /// <param name="zoneAuthored">When true, do not reflect the buff create back to Zone.</param>
    public void ApplyOnSpawnSkillBuffs(bool zoneAuthored = true)
    {
        var skills = NpcGameData.Instance.GetNpSkills(TemplateId, SkillUseConditionKind.OnSpawn);
        if (skills == null || skills.Count == 0)
            return;

        var applied = 0;
        foreach (var npcSkill in skills)
        {
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(npcSkill.SkillId);
            if (skillTemplate?.Effects == null || skillTemplate.Effects.Count == 0)
                continue;

            foreach (var skillEffect in skillTemplate.Effects)
            {
                if (skillEffect?.Template is not BuffEffect buffEffect || buffEffect.Buff == null)
                    continue;
                if (Buffs.CheckBuff(buffEffect.Buff.Id))
                    continue;

                Buffs.AddBuff(new Buff(
                    this,
                    this,
                    new SkillCasterUnit(ObjId),
                    buffEffect.Buff,
                    null,
                    DateTime.UtcNow)
                {
                    ZoneAuthored = zoneAuthored
                });
                applied++;
            }
        }

        if (applied > 0)
            Logger.Info(
                "OnSpawn skill FX buffs applied bc={0} tpl={1} count={2} zoneAuthored={3}",
                ObjId, TemplateId, applied, zoneAuthored);
    }

    /// <summary>
    /// SC-only paint for OnSpawn skills that carry <c>fire_anim</c> and no plot.
    /// Ghost-army units (e.g. skill 21363 / fire_anim 374) have no BuffEffects, so
    /// <see cref="ApplyOnSpawnSkillBuffs"/> alone leaves them popping in.
    /// Never calls <c>Skill.Use</c> — that duals dedic OnSpawn under ZoneAuthority (plots/WZ
    /// once crashed zone 257 after skill 15311→15340). Kill: <c>AAEMU_DISABLE_ONSPAWN_FIRE_ANIM=1</c>.
    /// </summary>
    public void CastOnSpawnAnimationSkills()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("AAEMU_DISABLE_ONSPAWN_FIRE_ANIM"),
                "1",
                StringComparison.Ordinal))
            return;

        var skills = NpcGameData.Instance.GetNpSkills(TemplateId, SkillUseConditionKind.OnSpawn);
        if (skills == null || skills.Count == 0)
            return;

        var fired = 0;
        foreach (var npcSkill in skills)
        {
            var st = SkillManager.Instance.GetSkillTemplate(npcSkill.SkillId);
            if (st == null)
                continue;
            // Plots stay on dedic; World must not re-run plot graphs under ZoneAuthority.
            if (st.Plot != null || st.PlotOnly)
                continue;

            var animId = st.FireAnim?.Id ?? 0;
            if (animId == 0)
                continue;

            var skill = new Skill(st) { TlId = SkillTlIdManager.GetNextId(this) };
            try
            {
                var caster = new SkillCasterUnit(ObjId);
                var target = new SkillCastUnitTarget(ObjId);
                var skillObject = new SkillObject();
                // Instant cast (casting_time=0): Fired + fireAnim is enough; no Zone WZ bridge.
                BroadcastPacket(
                    new SCSkillFiredPacket(st.Id, skill.TlId, caster, target, skill, skillObject)
                    {
                        FireAnimId = animId,
                        EffectDelayMs = st.EffectDelay
                    },
                    true);
                BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);
                fired++;
            }
            finally
            {
                if (skill.TlId != 0)
                {
                    SkillTlIdManager.ReleaseId(skill.TlId);
                    skill.TlId = 0;
                }
            }
        }

        if (fired > 0)
        {
            Logger.Info(
                "OnSpawn fire_anim SC-only bc={0} tpl={1} count={2}",
                ObjId, TemplateId, fired);
        }
    }

    /// <summary>
    /// ZoneAuthority: dedic rarely starts plot_only OnSpawn for tower stage units (e.g. skill
    /// 15298 on tpl 8830 → plot summons 8826/8834). World runs those plots so army packs appear.
    /// Suppresses WZ skill relay so dedic does not dual-fire. Kill: <c>AAEMU_DISABLE_ONSPAWN_PLOTS=1</c>.
    /// Gated to priority/event mirrors — not ambient plot_only on_spawn (318 rows).
    /// </summary>
    public void CastOnSpawnPlotSkills()
    {
        if (!WorldIntegration.ZoneAuthority)
            return;
        if (string.Equals(
                Environment.GetEnvironmentVariable("AAEMU_DISABLE_ONSPAWN_PLOTS"),
                "1",
                StringComparison.Ordinal))
            return;
        if (!IsZoneMirror || !IsMirrorStreamPriority)
            return;

        var skills = NpcGameData.Instance.GetNpSkills(TemplateId, SkillUseConditionKind.OnSpawn);
        if (skills == null || skills.Count == 0)
            return;

        var started = 0;
        foreach (var npcSkill in skills)
        {
            var skill = SkillManager.Instance.GetNpSkillTemplate(npcSkill);
            if (skill?.Template == null)
                continue;
            // Only true plot graphs. plot_only with no plot is a no-op for army work.
            if (skill.Template.Plot == null || !skill.Template.PlotOnly)
                continue;

            if (Cooldowns.CheckCooldown(skill.Id))
                continue;
            if (skill.Template.CooldownTime == 0)
                Cooldowns.AddCooldown(skill.Id, uint.MaxValue);

            // skill_use_param1: delay seconds before OnSpawn graph (Crimson stage uses 1.0).
            var delaySec = npcSkill.SkillUseParam1;
            skill.SuppressZoneSkillRelay = true;
            var caster = SkillCaster.GetByType(SkillCasterType.Unit);
            caster.ObjId = ObjId;
            var target = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            target.ObjId = ObjId;

            void Fire()
            {
                if (Hp <= 0 || !IsVisible)
                    return;
                try
                {
                    var result = skill.Use(this, caster, target, null, true, out _);
                    Logger.Info(
                        "OnSpawn plot_only Use bc={0} tpl={1} skill={2} plot={3} result={4}",
                        ObjId, TemplateId, skill.Id, skill.Template.Plot?.Id ?? 0, result);
                }
                catch (Exception ex)
                {
                    Logger.Warn(
                        ex,
                        "OnSpawn plot_only failed bc={0} tpl={1} skill={2}",
                        ObjId, TemplateId, skill.Id);
                }
            }

            if (delaySec > 0.01f)
            {
                var delay = TimeSpan.FromSeconds(delaySec);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(delay).ConfigureAwait(false);
                        Fire();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "OnSpawn plot_only delay fire bc={0} skill={1}", ObjId, skill.Id);
                    }
                });
            }
            else
            {
                Fire();
            }

            started++;
        }

        if (started > 0)
        {
            Logger.Info(
                "OnSpawn plot_only scheduled bc={0} tpl={1} count={2}",
                ObjId, TemplateId, started);
        }
    }

    public override void Delete()
    {
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

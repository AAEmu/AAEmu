using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Mate;
using AAEmu.Game.Utils;
using Task = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Models.Game.Units;

public class MatePassengerInfo
{
    public uint _objId;
    public AttachUnitReason _reason;
}

public sealed class Mate : Unit
{
    public override UnitTypeFlag TypeFlag { get => UnitTypeFlag.Mate; }
    public override BaseUnitType BaseUnitType => BaseUnitType.Mate;
    public NpcTemplate Template { get; set; }
    public uint OwnerObjId { get; set; }
    public Dictionary<AttachPointKind, MatePassengerInfo> Passengers { get; }
    public override float Scale => Template.Scale;

    /// <summary>Zone key that currently owns this mate's synchronized unit state.</summary>
    public uint ZoneAnnouncedTo { get; set; }

    /// <summary>
    /// Combat chase uses the actor model's run stance, same source as <see cref="NPChar.Npc.BaseMoveSpeed"/>.
    /// </summary>
    public override float BaseMoveSpeed
    {
        get
        {
            var model = ModelManager.Instance.GetActorModel(ModelId);
            if (model == null || !model.Stances.TryGetValue(GameStanceType.Combat, out var stance))
                return 1f;
            return Math.Min(stance.AiMoveSpeedRun, stance.MaxSpeed);
        }
    }

    /// <summary>
    /// The item that this summon is from
    /// </summary>
    public ulong ItemId { get; set; }

    /// <summary>
    /// enum_mate_types: 1 ride, 2 battle. Resolved from the npc's mate_equip_slot_pack.
    /// </summary>
    public byte MateType { get; set; }

    public byte UserState { get; set; }
    public int Experience { get; set; }
    public int Mileage { get; set; }
    public uint SpawnDelayTime { get; set; }
    public List<uint> Skills { get; set; }
    public MateDb DbInfo { get; set; }
    public Task MateXpUpdateTask { get; set; }
    public bool IsMaxLevel => Level >= ExperienceManager.Instance.MaxMateLevel;

    #region Attributes

    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var result = formula.Evaluate(parameters);
            var res = (int)result;
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Str;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Dex);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Dex;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Sta);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Sta;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Int);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Int;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Spi);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = (int)formula.Evaluate(parameters);
            //foreach (var item in Inventory.Equip)
            //    if (item is EquipItem equip)
            //        res += equip.Spi;
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Fai);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxHealth);
            var mateKindVariable = FormulaManager.Instance.GetUnitVariable(formula.Id,
                UnitFormulaVariableType.MateKind, (uint)Template.MateKindId);

            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = mateKindVariable
            };
            var res = (int)formula.Evaluate(parameters);

            res = (int)CalculateWithBonuses(res, UnitAttribute.MaxHealth);

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.HealthRegen)]
    public override int HpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.HealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
            };
            // compact.sqlite3 unit_formulas kind 31 owner Mate: (sta * 0.1) * 2 — same shape as
            // Slave (no post-divide). The old /= 5 zeroed low-Sta ticks and stalled pet HP bars.
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return Math.Max(res, 1);
        }
    }

    [UnitAttribute(UnitAttribute.MaxMana)]
    public override int MaxMp
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxMana);
            var mateKindVariable = FormulaManager.Instance.GetUnitVariable(formula.Id,
                UnitFormulaVariableType.MateKind, (uint)Template.MateKindId);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = mateKindVariable
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.ManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["mate_kind"] = Template.MateKindId
            };
            var res = (int)formula.Evaluate(parameters);
            res /= 5; // TODO ...
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

    // [UnitAttribute(UnitAttribute.Dps)]
    public override float LevelDps
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["ab_level"] = Level
            };

            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public override int DpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MeleeDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
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

    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public override int MDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.SpellDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Armor);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MagicResist);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
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
    #endregion

    public Mate()
    {
        Skills = [];
        Passengers = [];
        Equipment = new MateEquipmentContainer(0, SlotType.EquipmentMate, false, this);

        // TODO: Spawn this with the correct amount of seats depending on the template
        // 2 seats by default
        Passengers.Add(AttachPointKind.Driver, new MatePassengerInfo { _objId = 0, _reason = 0 });
        Passengers.Add(AttachPointKind.Passenger0, new MatePassengerInfo { _objId = 0, _reason = 0 });
    }

    /// <summary>
    /// Update the Item Data if it was summoned by an item
    /// </summary>
    private void UpdateMateItemData()
    {
        if (ItemId > 0)
        {
            var item = ItemManager.Instance.GetItemByItemId(ItemId);
            if (item is SummonMate mateItem)
            {
                mateItem.DetailMateExp = Experience;
                mateItem.DetailLevel = Level;
                mateItem.IsDirty = true;
            }
        }
    }

    /// <summary>
    /// Adds exp to this Mate and checks for level ups
    /// </summary>
    /// <param name="expDelta">The change in experience.</param>
    /// <remarks>This method does nothing if <paramref name="expDelta"/> is negative or zero. Only a positive increase in experience can be applied.</remarks>
    public void AddExp(int expDelta)
    {
        if (expDelta <= 0)
            return;
        if (IsMaxLevel)
            return;

        expDelta = (int)Math.Round(AppConfiguration.Instance.World.ExpRate * expDelta);
        var newExperience = Experience + expDelta;
        var newLevel = ExperienceManager.Instance.GetLevelFromExp(newExperience, Level, out var overflow, true);
        var leveledUp = newLevel > Level;

        // Prevent overflow - cap the experience at the amount for the highest level
        if (newLevel >= ExperienceManager.Instance.MaxMateLevel)
        {
            newExperience -= overflow;
        }

        Experience = newExperience;
        Level = newLevel;

        UpdateMateItemData();
        DbInfo.Xp = Experience;
        DbInfo.Level = Level;

        var owner = WorldManager.Instance.GetCharacterByObjId(OwnerObjId);
        owner.SendPacket(new SCExpChangedPacket(ObjId, expDelta, false));

        if (leveledUp)
        {
            Hp = MaxHp;
            Mp = MaxMp;
            BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
            owner.SendPacket(new SCUnitStatePacket(this));
            owner.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            if (WorldIntegration.ZoneAuthority)
            {
                WorldIntegration.RelayLevelChangedToZone?.Invoke(ObjId, Level);
                WorldIntegration.RelayUnitPointsToZone?.Invoke(ObjId, Hp, Mp);
            }
            // Notify owner of the level up event
            owner.Events.OnMateLevelUp(this, new OnMateLevelUpArgs());
        }
    }

    public override void AddVisibleObject(Character character)
    {
        base.AddVisibleObject(character);

        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCMateStatePacket(ObjId));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));

        // Initialize faction for a newly visible mate.
        if (Faction != null)
            character.SendPacket(new SCUnitFactionChangedPacket(
                ObjId, Name ?? "", FactionsEnum.Invalid, Faction.Id, false));

        foreach (var ati in Passengers)
        {
            if (ati.Value._objId > 0)
            {
                var player = WorldManager.Instance.GetCharacterByObjId(ati.Value._objId);
                if (player != null)
                    character.SendPacket(new SCUnitAttachedPacket(player.ObjId, ati.Key, ati.Value._reason, ObjId));
            }
        }
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    /// <summary>Starts server-paced attacks on <see cref="Unit.CurrentTarget"/>.</summary>
    public void StartOrderedAttack()
    {
        if (CurrentTarget is not Unit target || target.Hp <= 0 || !CanAttack(target))
        {
            StopOrderedAttack();
            return;
        }

        var skillId = Template?.BaseSkillId > 0 ? (uint)Template.BaseSkillId : 2u;
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return;

        if (AutoAttackTask != null)
        {
            if (!AutoAttackTask.Cancelled && AutoAttackTask.Skill?.Template?.Id == skillId)
                return;
            StopOrderedAttack();
        }

        IsInBattle = true;
        LastCombatActivity = DateTime.UtcNow;
        var skill = new Skill(template);
        var task = new UseMateAutoAttackSkillTask(skill, this);
        IsAutoAttack = true;
        AutoAttackTask = task;
        // Chase needs a short period; weapon delay alone left the pet stranded out of melee.
        var delayMs = Math.Min(200.0, SkillManager.GetAttackDelay(template, this));
        TaskManager.Instance.Schedule(task, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(delayMs), -1);
    }

    public void StopOrderedAttack()
    {
        if (AutoAttackTask == null)
        {
            IsAutoAttack = false;
            return;
        }

        var skillId = AutoAttackTask.Skill?.Template?.Id ?? 0;
        if (AutoAttackTask.Skill != null)
            AutoAttackTask.Skill.Cancelled = true;
        AutoAttackTask.Cancelled = true;
        AutoAttackTask.Cancel();
        AutoAttackTask = null;
        IsAutoAttack = false;
        // Ordered-attack sets IsInBattle for PersistentHpRegen; clear it when the order ends so
        // out-of-combat HealthRegen (spi*0.1+7) resumes instead of waiting on combat timeout.
        IsInBattle = false;
        if (skillId != 0)
            BroadcastPacket(new SCSkillStoppedPacket(ObjId, skillId), true);
    }

    /// <summary>Steps toward a world position and synchronizes the movement with the active zone.</summary>
    public bool MoveTowards(Vector3 other, float distance, byte actorFlags = 4, float rangeTolerance = 1f)
    {
        distance *= MoveSpeedMul;
        if (distance < 0.01f)
            return false;

        if (Buffs.HasEffectsMatchingCondition(e =>
                e.Template.Stun
                || e.Template.Sleep
                || e.Template.Root
                || e.Template.Knockdown
                || e.Template.Fastened)
            || IsDead)
            return false;

        if ((ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return false;

        var oldPosition = Transform.Local.ClonePosition();
        var targetDist = MathUtil.CalculateDistance(Transform.Local.Position, other, true);
        if (targetDist <= rangeTolerance)
            return true;

        var travelDist = Math.Min(targetDist, distance);
        var (newX, newY, newZ) = World.Transform.PositionAndRotation.AddDistanceToFront(
            travelDist, targetDist, Transform.Local.Position, other);
        var targetPositionZ = WorldManager.Instance.GetHeight(Transform.ZoneId, newX, newY, newZ);
        Transform.Local.SetPosition(newX, newY, targetPositionZ);
        Transform.FinalizeTransform();

        var angle = MathUtil.CalculateAngleFrom(Transform.Local.Position, other);
        var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
        Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
        var (rx, ry, rz) = Transform.Local.ToRollPitchYawSBytesMovement();

        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = Transform.Local.Position.X;
        moveType.Y = Transform.Local.Position.Y;
        moveType.Z = Transform.Local.Position.Z;
        moveType.VelX = (short)velX;
        moveType.VelY = (short)velY;
        moveType.RotationX = rx;
        moveType.RotationY = ry;
        moveType.RotationZ = rz;
        moveType.ActorFlags = actorFlags;
        moveType.Flags = MoveTypeFlags.Moving | (IsInBattle ? MoveTypeFlags.InCombat : 0);
        moveType.DeltaMovement = [0, 127, 0];
        moveType.Stance = GameStanceType.Combat;
        moveType.Alertness = MoveTypeAlertness.Combat;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        CheckMovedPosition(oldPosition);

        if (WorldIntegration.ZoneAuthority && WorldIntegration.RelayMoveToZone != null)
        {
            var moveBody = new PacketStream();
            moveBody.Write((byte)moveType.Type);
            moveBody.Write(moveType);
            WorldIntegration.RelayMoveToZone(ObjId, moveBody.GetBytes());
        }
        else
        {
            BroadcastPacket(new SCOneUnitMovementPacket(ObjId, moveType), false);
        }

        return false;
    }

    public override int DoFallDamage(float impactSpeed)
    {
        var fallDmg = base.DoFallDamage(impactSpeed);
        if (Hp <= 0)
        {
            var riders = Passengers.ToList();
            // When fall damage kills a mount, also kill all of it's riders
            for (var i = riders.Count - 1; i >= 0; i--)
            {
                var pos = riders[i].Key;
                var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value._objId);
                if (rider != null)
                {
                    rider.DoFallDamage(impactSpeed);
                    if (rider.Hp <= 0)
                        rider.ParentWorld.MateManager.UnMountMate(rider, TlId, pos, AttachUnitReason.SlaveBinding);
                }
            }
        }

        return fallDmg;
    }

    protected override void RegenTick(TimeSpan delta)
    {
        if (!NeedsRegen)
        {
            return;
        }
        if (IsDead)
        {
            var riders = Passengers.ToList();
            for (var i = riders.Count - 1; i >= 0; i--)
            {
                var pos = riders[i].Key;
                var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value._objId);
                rider?.ParentWorld.MateManager.UnMountMate(rider, TlId, pos, AttachUnitReason.None);
            }
            return;
        }

        var oldHp = Hp;

        if (IsInBattle)
        {
            Hp += PersistentHpRegen;
            Mp += PersistentMpRegen;
        }
        else
        {
            Hp += HpRegen;
            Mp += MpRegen;
        }

        Hp = Math.Min(Hp, MaxHp);
        Mp = Math.Min(Mp, MaxMp);
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp), false);
        // BroadcastPacket(self:false) only hits GetAround characters; also push to owner so the
        // pet frame updates when regen ticks (owner can sit outside the mate's around radius).
        var owner = WorldManager.Instance.GetCharacterByObjId(OwnerObjId);
        owner?.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        // Zone mirror HP — same WZUnitPoints path as HealEffect / ZoneAuthorityCombat.
        WorldIntegration.RelayUnitPointsToZone?.Invoke(ObjId, Hp, Mp);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
    }

    public void StartUpdateXp(Character owner)
    {
        if (MateXpUpdateTask != null)
        {
            return;
        }
        if (IsMaxLevel)
            return;
        MateXpUpdateTask = new MateXpUpdateTask(owner, this);
        TaskManager.Instance.Schedule(MateXpUpdateTask, TimeSpan.FromSeconds(60));
        //Logger.Trace("[StartUpdateXp] The current timer has been started...");
    }

    public void StopUpdateXp()
    {
        MateXpUpdateTask?.Cancel();
        MateXpUpdateTask = null;
        //Logger.Trace("[StopUpdateXp] The current timer has been canceled...");
    }

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit

        if (Passengers.Count <= 0)
        {
            return;
        }

        foreach (var (_, passengerInfo) in Passengers)
        {
            var passenger = WorldManager.Instance.GetCharacterByObjId(passengerInfo._objId);
            passenger?.OnZoneChange(lastZoneKey, newZoneKey);
        }
    }

    public override Character GetOwnerCharacter()
    {
        var ownerObject = OwnerObjId > 0 ? ParentWorld.GetGameObject(OwnerObjId) as BaseUnit : null;
        return ownerObject?.GetOwnerCharacter();
    }
}

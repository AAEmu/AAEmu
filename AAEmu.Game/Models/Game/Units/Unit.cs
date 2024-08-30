using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units;

public class Unit : BaseUnit, IUnit
{
    public virtual UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.None;
    public virtual BaseUnitType BaseUnitType { get; set; } = BaseUnitType.Invalid;

    public virtual UnitEvents Events { get; }
    private Task _regenTask;
    public uint ModelId { get; set; }
    public SkillController ActiveSkillController { get; set; }

    public override float ModelSize
    {
        get
        {
            return (ModelManager.Instance.GetActorModel(ModelId)?.Radius ?? 0) * Scale;
        }
    }

    public virtual float BaseMoveSpeed
    {
        get
        {
            return 1f;
        }
    }

    public byte Level { get; set; }
    public byte HeirLevel { get; set; } // hierarchy level for 3.0.3.0 или Уровень Наследника
    public uint HeirExp { get; set; } // heirExp add for 3.5.0.3 : uint in 3.5, 4.5, long in 5.7

    public int Hp { get; set; }

    public int HighAbilityRsc { get; set; }

    public int Hpp
    {
        get
        {
            if (MaxHp <= 0)
                return 0;
            return Math.Clamp((int)Math.Ceiling(Hp * 100f / MaxHp), 0, 100);
        }
    }

    public DateTime LastCombatActivity { get; set; }

    protected bool _isUnderWater;

    public virtual bool IsUnderWater
    {
        get => _isUnderWater;
        set => _isUnderWater = value;
    }

    /// <summary>
    /// List of values in the range of 0 -> 100
    /// </summary>
    protected List<int> HpTriggerPointsPercent { get; set; } = new();

    #region Attributes

    [UnitAttribute(UnitAttribute.MoveSpeedMul)]
    public virtual float MoveSpeedMul { get => (float)CalculateWithBonuses(1000f, UnitAttribute.MoveSpeedMul) / 1000f; }
    [UnitAttribute(UnitAttribute.GlobalCooldownMul)]
    public virtual float GlobalCooldownMul { get; set; } = 100f;
    [UnitAttribute(UnitAttribute.MaxHealth)]
    public virtual int MaxHp { get; set; }
    [UnitAttribute(UnitAttribute.HealthRegen)]
    public virtual int HpRegen { get; set; }
    [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
    public virtual int PersistentHpRegen { get; set; } = 30;
    public int Mp { get; set; }
    [UnitAttribute(UnitAttribute.MaxMana)]
    public virtual int MaxMp { get; set; }
    [UnitAttribute(UnitAttribute.ManaRegen)]
    public virtual int MpRegen { get; set; }
    [UnitAttribute(UnitAttribute.PersistentManaRegen)]
    public virtual int PersistentMpRegen { get; set; } = 30;
    [UnitAttribute(UnitAttribute.CastingTimeMul)]
    public virtual float CastTimeMul { get; set; } = 1f;
    public virtual float LevelDps { get; set; }
    [UnitAttribute(UnitAttribute.MainhandDps)]
    public virtual int Dps { get; set; }
    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public virtual int DpsInc { get; set; }
    [UnitAttribute(UnitAttribute.OffhandDps)]
    public virtual int OffhandDps { get; set; }
    [UnitAttribute(UnitAttribute.RangedDps)]
    public virtual int RangedDps { get; set; }
    [UnitAttribute(UnitAttribute.RangedDpsInc)]
    public virtual int RangedDpsInc { get; set; }
    [UnitAttribute(UnitAttribute.SpellDps)]
    public virtual int MDps { get; set; }
    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public virtual int MDpsInc { get; set; }
    [UnitAttribute(UnitAttribute.HealDps)]
    public virtual int HDps { get; set; }
    [UnitAttribute(UnitAttribute.HealDpsInc)]
    public virtual int HDpsInc { get; set; }
    [UnitAttribute(UnitAttribute.MeleeAntiMissMul)]
    public virtual float MeleeAccuracy { get; set; } = 100f;
    [UnitAttribute(UnitAttribute.MeleeCritical)]
    public virtual float MeleeCritical { get; set; }
    [UnitAttribute(UnitAttribute.MeleeCriticalBonus)]
    public virtual float MeleeCriticalBonus { get; set; }
    [UnitAttribute(UnitAttribute.MeleeCriticalMul)]
    public virtual float MeleeCriticalMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.RangedAntiMiss)]
    public virtual float RangedAccuracy { get; set; } = 100f;
    [UnitAttribute(UnitAttribute.RangedCritical)]
    public virtual float RangedCritical { get; set; }
    [UnitAttribute(UnitAttribute.RangedCriticalBonus)]
    public virtual float RangedCriticalBonus { get; set; }
    [UnitAttribute(UnitAttribute.RangedCriticalMul)]
    public virtual float RangedCriticalMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.SpellAntiMiss)]
    public virtual float SpellAccuracy { get; set; } = 100f;
    [UnitAttribute(UnitAttribute.SpellCritical)]
    public virtual float SpellCritical { get; set; }
    [UnitAttribute(UnitAttribute.SpellCriticalBonus)]
    public virtual float SpellCriticalBonus { get; set; }
    [UnitAttribute(UnitAttribute.SpellCriticalMul)]
    public virtual float SpellCriticalMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.HealCritical)]
    public virtual float HealCritical { get; set; }
    [UnitAttribute(UnitAttribute.HealCriticalBonus)]
    public virtual float HealCriticalBonus { get; set; }
    [UnitAttribute(UnitAttribute.HealCriticalMul)]
    public virtual float HealCriticalMul { get; set; }
    [UnitAttribute(UnitAttribute.Armor)]
    public virtual int Armor { get; set; }
    [UnitAttribute(UnitAttribute.MagicResist)]
    public virtual int MagicResistance { get; set; }
    [UnitAttribute(UnitAttribute.IgnoreArmor)]
    public virtual int DefensePenetration { get; set; }
    [UnitAttribute(UnitAttribute.MagicPenetration)]
    public virtual int MagicPenetration { get; set; }
    [UnitAttribute(UnitAttribute.Dodge)]
    public virtual float DodgeRate { get; set; }
    [UnitAttribute(UnitAttribute.MeleeParry)]
    public virtual float MeleeParryRate { get; set; }
    [UnitAttribute(UnitAttribute.RangedParry)]
    public virtual float RangedParryRate { get; set; }
    [UnitAttribute(UnitAttribute.Block)]
    public virtual float BlockRate { get; set; }
    [UnitAttribute(UnitAttribute.BattleResist)]
    public virtual int BattleResist { get; set; }
    [UnitAttribute(UnitAttribute.BullsEye)]
    public virtual int BullsEye { get; set; }
    [UnitAttribute(UnitAttribute.Flexibility)]
    public virtual int Flexibility { get; set; }
    [UnitAttribute(UnitAttribute.Facets)]
    public virtual int Facets { get; set; }
    [UnitAttribute(UnitAttribute.MeleeDamageMul)]
    public virtual float MeleeDamageMul { get; set; } = 1.0f;
    [UnitAttribute(UnitAttribute.RangedDamageMul)]
    public virtual float RangedDamageMul { get; set; } = 1.0f;
    [UnitAttribute(UnitAttribute.SpellDamageMul)]
    public virtual float SpellDamageMul { get; set; } = 1.0f;

    [UnitAttribute(UnitAttribute.IncomingHealMul)]
    public virtual float IncomingHealMul { get; set; } = 1.0f;
    [UnitAttribute(UnitAttribute.HealMul)]
    public virtual float HealMul { get; set; } = 1.0f;
    [UnitAttribute(UnitAttribute.IncomingDamageMul)]
    public virtual float IncomingDamageMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.IncomingMeleeDamageMul)]
    public virtual float IncomingMeleeDamageMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.IncomingRangedDamageMul)]
    public virtual float IncomingRangedDamageMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.IncomingSpellDamageMul)]
    public virtual float IncomingSpellDamageMul { get; set; } = 1f;
    [UnitAttribute(UnitAttribute.AggroMul)]
    public float AggroMul
    {
        get => (float)CalculateWithBonuses(100d, UnitAttribute.AggroMul);
    }
    [UnitAttribute(UnitAttribute.IncomingAggroMul)]

    #endregion Attributes

    public float IncomingAggroMul
    {
        get => (float)CalculateWithBonuses(100d, UnitAttribute.IncomingAggroMul);
    }
    public BaseUnit CurrentTarget { get; set; }
    public BaseUnit CurrentInteractionObject { get; set; }
    public virtual byte RaceGender => 0;
    public virtual UnitCustomModelParams ModelParams { get; set; }
    public byte ActiveWeapon { get; set; }
    public bool IdleStatus { get; set; }
    public bool ForceAttack { get; set; }
    public bool Invisible { get; set; }
    public uint OwnerId { get; set; }
    public SkillTask SkillTask { get; set; }
    public SkillTask AutoAttackTask { get; set; }
    public DateTime GlobalCooldown { get; set; }
    public bool IsGlobalCooldowned => GlobalCooldown > DateTime.UtcNow;
    public object GcdLock { get; set; }
    public DateTime SkillLastUsed { get; set; }
    public PlotState ActivePlotState { get; set; }
    public Dictionary<uint, List<Bonus>> Bonuses { get; set; }
    public UnitCooldowns Cooldowns { get; set; }
    public virtual Expedition Expedition { get; set; }

    public bool IsInBattle
    {
        get => _isInBattle;
        set
        {
            if (value == _isInBattle)
                return;
            _isInBattle = value;
            if (!_isInBattle)
                BroadcastPacket(new SCCombatClearedPacket(ObjId), true);
        }
    }
    private bool _isInBattle;

    public bool IsInDuel { get; set; }
    public bool IsInPatrol { get; set; } // so as not to run the route a second time
    public int SummarizeDamage { get; set; }
    public bool IsAutoAttack { get; set; }
    public ushort TlId { get; set; }
    public ItemContainer Equipment { get; set; }
    public GameConnection Connection { get; set; }

    /// <summary>
    /// Unit巡逻
    /// Unit patrol
    /// 指明Unit巡逻路线及速度、是否正在执行巡逻等行为
    /// Indicates the route and speed of the Unit patrol, whether it is performing patrols, etc.
    /// </summary>
    public Patrol Patrol { get; set; }
    public Simulation Simulation { get; set; }

    public UnitProcs Procs { get; protected set; }

    public bool ConditionChance { get; set; }

    public Unit()
    {
        Events = new UnitEvents();
        GcdLock = new object();
        Bonuses = new Dictionary<uint, List<Bonus>>();
        IsInBattle = false;
        Equipment = new EquipmentContainer(0, SlotType.Equipment, false, this);
        ChargeLock = new object();
        Cooldowns = new UnitCooldowns();
    }

    public void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
    {
        SetPosition(x, y, z, (float)MathUtil.ConvertDirectionToRadian(rotationX), (float)MathUtil.ConvertDirectionToRadian(rotationY), (float)MathUtil.ConvertDirectionToRadian(rotationZ));
    }

    public override void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ)
    {
        var moved = !Transform.World.Position.X.Equals(x) || !Transform.World.Position.Y.Equals(y) || !Transform.World.Position.Z.Equals(z);
        if (moved)
        {
            Events.OnMovement(this, new OnMovementArgs());
        }
        base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);

        var worldDrownThreshold = WorldManager.Instance.GetWorld(Transform.WorldId)?.OceanLevel - 2f ?? 98f;
        if (!IsUnderWater && Transform.World.Position.Z < worldDrownThreshold)
            IsUnderWater = true;
        else if (IsUnderWater && Transform.World.Position.Z > worldDrownThreshold)
            IsUnderWater = false;
    }

    public bool CheckMovedPosition(Vector3 oldPosition)
    {
        var moved = !Transform.World.Position.X.Equals(oldPosition.X) || !Transform.World.Position.Y.Equals(oldPosition.Y) || !Transform.World.Position.Z.Equals(oldPosition.Z);
        if (moved)
        {
            Events.OnMovement(this, new OnMovementArgs());
        }
        if (DisabledSetPosition)
            return moved;

        WorldManager.Instance.AddVisibleObject(this);
        // base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);
        return moved;
    }

    /// <summary>
    /// Make unit take value amount of damage, calls PostReduceCurrentHp() at the end
    /// </summary>
    /// <param name="attacker"></param>
    /// <param name="value"></param>
    /// <param name="killReason"></param>
    public virtual void ReduceCurrentHp(BaseUnit attacker, int value, KillReason killReason = KillReason.Damage)
    {
        if (Hp <= 0)
            return;

        var oldHp = Hp;

        var absorptionEffects = Buffs.GetAbsorptionEffects().ToList();
        if (absorptionEffects.Count > 0)
        {
            // Handle damage absorb
            foreach (var absorptionEffect in absorptionEffects)
            {
                value = absorptionEffect.ConsumeCharge(value);
            }
        }

        Hp = Math.Max(Hp - value, 0);

        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Hp > 0 ? Mp : 0, HighAbilityRsc), true);

        PostUpdateCurrentHp(attacker, oldHp, Hp, killReason);
    }

    /// <summary>
    /// Called at the end of ReduceCurrentHp() and can be overriden and handles things like death
    /// </summary>
    /// <param name="attackerBase"></param>
    /// <param name="oldHpValue"></param>
    /// <param name="newHpValue"></param>
    /// <param name="killReason"></param>
    public virtual void PostUpdateCurrentHp(BaseUnit attackerBase, int oldHpValue, int newHpValue, KillReason killReason = KillReason.Damage)
    {
        // If Hp triggers are set up, do the calculations for them
        if (HpTriggerPointsPercent.Count > 0)
        {
            var oldHpP = (int)Math.Round(oldHpValue * 100f / MaxHp);
            var newHpP = (int)Math.Round(newHpValue * 100f / MaxHp);

            if (oldHpP > newHpP)
            {
                // Took damage, check downwards
                foreach (var triggerValue in HpTriggerPointsPercent)
                {
                    if ((oldHpP > triggerValue) && (newHpP <= triggerValue))
                    {
                        DoHpChangeTrigger(triggerValue, true, oldHpValue, newHpValue);
                        break;
                    }
                }
            }

            if (oldHpP < newHpP)
            {
                // Healed, check upwards
                foreach (var triggerValue in HpTriggerPointsPercent)
                {
                    if ((oldHpP < triggerValue) && (newHpP >= triggerValue))
                    {
                        DoHpChangeTrigger(triggerValue, false, oldHpValue, newHpValue);
                        break;
                    }
                }
            }
        }

        if (Hp > 0)
            return;

        if (attackerBase is Unit attackerUnit)
        {
            attackerUnit.Events.OnKill(attackerUnit, new OnKillArgs { Target = attackerUnit });

            var world = WorldManager.Instance.GetWorld(Transform.WorldId);
            if (Transform.WorldId > 0)
            {
                var dungeon = IndunManager.Instance.GetDungeonByWorldId(Transform.WorldId);
                if (dungeon is not null)
                {
                    world.Events.OnUnitKilled(world, new OnUnitKilledArgs { Killer = attackerUnit, Victim = this });
                    world.Events.OnUnitCombatEnd(world, new OnUnitCombatEndArgs { Npc = this });
                    Events.OnDeath(this, new OnDeathArgs { Killer = attackerUnit, Victim = this });
                }
            }
        }

        DoDie(attackerBase, killReason);
    }

    protected virtual void DoHpChangeTrigger(int triggerValue, bool tookDamage, int oldHpValue, int newHpValue)
    {
        // Do nothing by default
    }

    public virtual void ReduceCurrentMp(BaseUnit unit, int value)
    {
        if (Hp == 0)
        {
            return;
        }

        Mp = Math.Max(Mp - value, 0);
        //if (Mp == 0)
        //{
        //    StopRegen();
        //}

        //else
        //StartRegen();
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc), true);
    }

    public virtual void DoDie(BaseUnit killer, KillReason killReason)
    {
        InterruptSkills();

        Events.OnDeath(this, new OnDeathArgs { Killer = (Unit)killer, Victim = this });
        Buffs.RemoveEffectsOnDeath();
        killer.BroadcastPacket(new SCUnitDeathPacket(ObjId, killReason, (Unit)killer), true);
        if (killer == this)
        {
            switch (this)
            {
                //case Npc:
                //    break;
                case Mate mate:
                    DespawnMate(WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId));
                    break;
                case Character character:
                    DespawnMate(character);
                    break;
                    //default:
                    //    break;
            }
            return;
        }

        var lootDropItems = ItemManager.Instance.CreateLootDropItems(ObjId, killer);
        // Without moving the tagging into the root of unit, we need to do some work for loot distribution:
        if (lootDropItems.Count > 0)
        {
            // Logger.Info($"Loot item count is {lootDropItems.Count}");
            var unit = WorldManager.Instance.GetNpc(ObjId);
            if (unit == null)
            {
                // Defaulting to the original code if this isn't an NPC
                // Logger.Info($"Not an NPC for {ObjId}");

                killer.BroadcastPacket(new SCLootableStatePacket(ObjId, true), true);

            }
            else
            {
                // it's an NPC, and we have a thing for this!
                var eligiblePlayers = new HashSet<Character>();
                if (unit.CharacterTagging.TagTeam != 0)
                {
                    // A team has tagging rights.
                    // TODO: Master Looter
                    var activeTeam = TeamManager.Instance.GetActiveTeam(unit.CharacterTagging.TagTeam);
                    if (activeTeam.LootingRule.LootMethod == 0)
                    {
                        // FFA Loot
                        foreach (var member in activeTeam.Members)
                        {
                            if (member?.Character == null)
                                continue;

                            if (GetDistanceTo(member.Character) <= 200)
                            {
                                eligiblePlayers.Add(member.Character);
                            }
                        }
                    }
                    else
                    {
                        // RoundRobin
                        // TODO: Master Looter
                        var nextEligibleLooter = TeamManager.Instance.GetNextEligibleLooter(unit.CharacterTagging.TagTeam, unit);
                        if (nextEligibleLooter != null)
                        {
                            eligiblePlayers.Add(nextEligibleLooter);
                            Logger.Warn($"Eligible team, adding {nextEligibleLooter}");
                        }
                        else
                        {
                            Logger.Warn("Next eligible looter was null");
                        }
                    }
                }
                else if (unit.CharacterTagging.Tagger != null)
                {
                    //A player has tag rights
                    eligiblePlayers.Add(unit.CharacterTagging.Tagger);
                    Logger.Warn($"Added tagger {unit.CharacterTagging.Tagger}");
                }
                if (eligiblePlayers.Count > 0)
                {
                    foreach (var eligible in eligiblePlayers)
                    {
                        if (eligible != null)
                        {
                            eligible.SendPacket(new SCLootableStatePacket(ObjId, true));
                        }
                        else
                        {
                            Logger.Warn($"Eligible player was null, eligible player count was {eligiblePlayers.Count}");
                        }
                    }
                }
            }

        }



        if (CurrentTarget != null)
        {
            killer.SendPacketToPlayers([this, killer], new SCUnitAiAggroPacket(killer.ObjId, 0));

            if (killer is Unit killerUnit)
            {
                killerUnit.SummarizeDamage = 0;
                if (killerUnit.CurrentTarget is Unit unitTarget)
                {
                    unitTarget.IsInBattle = false;
                }

                killerUnit.IsInBattle = false;
            }
            //killer.StartRegen();
            killer.BroadcastPacket(new SCTargetChangedPacket(killer.ObjId, 0), true);

            if (killer is Character character)
            {
                StopAutoSkill(character);
                character.IsInBattle = false; // we need the character to be "not in battle"
                DespawnMate(character);
            }
            else if (((Unit)killer).CurrentTarget is Character character2)
            {
                StopAutoSkill(character2);
                character2.IsInBattle = false; // we need the character to be "not in battle"
                character2.DeadTime = DateTime.UtcNow;
                DespawnMate(character2);
            }

            ((Unit)killer).CurrentTarget = null;
        }
    }

    public static void DespawnMate(Character character)
    {
        // if we died sitting on a horse
        if (character.Hp > 0) { return; }

        var mates = MateManager.Instance.GetActiveMates(character.ObjId);
        if (mates != null)
        {
            for (var i = 0; i < mates.Count; i++)
                character.Mates.DespawnMate(mates[i]);
        }
    }

    public static void DespawSlave(Character character)
    {
        // if we died sitting on a horse
        if (character.Hp > 0) { return; }

        SlaveManager.Instance.RemoveAndDespawnAllActiveOwnedSlaves(character);
    }

    public void StopAutoSkill(Unit unit)
    {
        if (unit.AutoAttackTask is null || !(unit is Character character))
        {
            return;
        }

        character.AutoAttackTask.Cancelled = true;
        // await character.AutoAttackTask.Cancel();
        /*
        character.AutoAttackTask = null;
        character.IsAutoAttack = false; // turned off auto attack
        character.BroadcastPacket(new SCSkillEndedPacket(character.TlId), true);
        character.BroadcastPacket(new SCSkillStoppedPacket(character.ObjId, character.SkillId), true);
        TlIdManager.Instance.ReleaseId(character.TlId);
        */
    }

    public void StartAutoSkill(Skill skill)
    {
        if (this is not Character character || AutoAttackTask is not null)
        {
            return;
        }

        var newTask = new UseAutoAttackSkillTask(skill, character);
        character.AutoAttackTask = newTask;
        var attackDelayTimes = SkillManager.GetAttackDelay(skill.Template, character); 

        TaskManager.Instance.Schedule(character.AutoAttackTask, TimeSpan.FromMilliseconds(attackDelayTimes),
            TimeSpan.FromMilliseconds(attackDelayTimes), -1);
        /*
        await character.AutoAttackTask.Cancel();
        character.AutoAttackTask = null;
        character.IsAutoAttack = false; // turned off auto attack
        character.BroadcastPacket(new SCSkillEndedPacket(character.TlId), true);
        character.BroadcastPacket(new SCSkillStoppedPacket(character.ObjId, character.SkillId), true);
        TlIdManager.Instance.ReleaseId(character.TlId);
        */
    }

    [Obsolete("This method is deprecated", false)]
    public void StartRegen()
    {
        if (_regenTask != null || Hp >= MaxHp && Mp >= MaxMp || Hp == 0)
        {
            return;
        }
        _regenTask = new UnitPointsRegenTask(this);
        TaskManager.Instance.Schedule(_regenTask, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    [Obsolete("This method is deprecated", false)]
    public void StopRegen()
    {
        if (_regenTask == null)
        {
            return;
        }
        _regenTask.Cancel();
        _regenTask = null;
    }

    public void SetInvisible(bool value)
    {
        Invisible = value;
        BroadcastPacket(new SCUnitInvisiblePacket(ObjId, Invisible), true);
    }

#pragma warning disable CA1822 // Mark members as static
    public void SetGeoDataMode(bool value)
    {
        AppConfiguration.Instance.World.GeoDataMode = value;
    }
    public void SetGodMode(bool value)
    {
        AppConfiguration.Instance.World.GodMode = value;
    }
    public void SetGrowthRate(float value)
    {
        AppConfiguration.Instance.World.GrowthRate = value;
    }
    public void SetLootRate(float value)
    {
        AppConfiguration.Instance.World.LootRate = value;
    }
    public void SetVocationRate(float value)
    {
        AppConfiguration.Instance.World.VocationRate = value;
    }
    public void SetHonorRate(float value)
    {
        AppConfiguration.Instance.World.HonorRate = value;
    }
    public void SetExpRate(float value)
    {
        AppConfiguration.Instance.World.ExpRate = value;
    }
    public void SetAutoSaveInterval(float value)
    {
        AppConfiguration.Instance.World.AutoSaveInterval = value;
    }
    public void SetLogoutMessage(string value)
    {
        AppConfiguration.Instance.World.LogoutMessage = value;
    }
    public void SetMotdMessage(string value)
    {
        AppConfiguration.Instance.World.MOTD = value;
    }
#pragma warning restore CA1822 // Mark members as static
    public void SetCriminalState(bool criminalState, BaseUnit attackedTarget)
    {
        if (criminalState)
        {
            // Don't trigger Retribution (purple) when target is a Npc (except for player portals)
            if ((attackedTarget is Npc) && (attackedTarget is not Portal))
                return;

            var buff = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Retribution);
            var casterObj = new SkillCasterUnit(ObjId);
            Buffs.AddBuff(new Buff(this, this, casterObj, buff, null, DateTime.UtcNow));
        }
        else
        {
            Buffs.RemoveBuff((uint)BuffConstants.Retribution);
        }
    }

    public void SetForceAttack(bool value)
    {
        ForceAttack = value;
        if (ForceAttack)
        {
            var buff = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Bloodlust);
            var casterObj = new SkillCasterUnit(ObjId);
            Buffs.AddBuff(new Buff(this, this, casterObj, buff, null, DateTime.UtcNow));
        }
        else
        {
            Buffs.RemoveBuff((uint)BuffConstants.Bloodlust);
        }
        BroadcastPacket(new SCForceAttackSetPacket(ObjId, ForceAttack), true);
    }

    public override void AddBonus(uint bonusIndex, Bonus bonus)
    {
        var bonuses = Bonuses.TryGetValue(bonusIndex, out var bonuse) ? bonuse : new List<Bonus>();
        bonuses.Add(bonus);
        Bonuses[bonusIndex] = bonuses;
    }

    public override void RemoveBonus(uint bonusIndex, UnitAttribute attribute)
    {
        if (!Bonuses.ContainsKey(bonusIndex))
        {
            return;
        }
        var bonuses = Bonuses[bonusIndex];
        foreach (var bonus in new List<Bonus>(bonuses))
        {
            if (bonus.Template != null && bonus.Template.Attribute == attribute)
            {
                bonuses.Remove(bonus);
            }
        }
    }

    public List<Bonus> GetBonuses(UnitAttribute attribute)
    {
        var result = new List<Bonus>();
        if (Bonuses == null)
        {
            return result;
        }
        foreach (var bonuses in new List<List<Bonus>>(Bonuses.Values))
        {
            foreach (var bonus in new List<Bonus>(bonuses))
            {
                if (bonus.Template != null && bonus.Template.Attribute == attribute)
                {
                    result.Add(bonus);
                }
            }
        }
        return result;
    }

    public double CalculateWithBonuses(double value, UnitAttribute attr)
    {
        foreach (var bonus in GetBonuses(attr))
        {
            if (bonus.Template.ModifierType == UnitModifierType.Percent)
                value += (value * bonus.Value / 100f);
            else
                value += bonus.Value;
        }
        return value;
    }

    public void SendPacket(GamePacket packet)
    {
        Connection?.SendPacket(packet);
    }

    public void SendErrorMessage(ErrorMessageType type)
    {
        SendPacket(new SCErrorMsgPacket(type, 0, true));
    }

    public virtual int GetAbLevel(AbilityType type)
    {
        return Level;
    }

    public string GetAttribute(UnitAttribute attr)
    {
        var props = GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

        if (props.Any())
            return props.ElementAt(0).GetValue(this).ToString();
        else
            return "NotFound";
    }

    public T GetAttribute<T>(UnitAttribute attr, T defaultVal)
    {
        var props = GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

        if (props.Any())
        {
            var ElementValue = props.ElementAt(0).GetValue(this);
            if (ElementValue is T ret)
                return ret;
        }
        return defaultVal;
    }

    public string GetAttribute(uint attr) => GetAttribute((UnitAttribute)attr);

    //Uncomment if you need this
    /*
    public string GetAttribute(string attr)
    {
        if (Enum.TryParse(typeof(UnitAttribute), attr, true, out var result))
        {
            return GetAttribute((UnitAttribute)result);
        }
        return "FailedParse";
    }
    */

    public override void InterruptSkills()
    {
        ActivePlotState?.RequestCancellation();
        if (SkillTask == null)
            return;
        switch (SkillTask)
        {
            case EndChannelingTask ect:
                ect.Skill.Stop(this, ect._channelDoodad);
                break;
            default:
                SkillTask.Skill.Stop(this);
                break;
        }
    }

    public bool IsDead
    {
        get
        {
            return Hp <= 0;
        }
    }

    public bool NeedsRegen
    {
        get
        {
            return Hp < MaxHp || Mp < MaxMp;
        }
    }

    // TODO: Implement this to grab actual loot info
    public virtual bool HasLootLeft { get; set; } = false;
    public virtual ModelPostureType ModelPostureType { get => ModelPostureType.None; }

    public virtual void OnSkillEnd(Skill skill)
    {

    }

    /// <summary>
    /// Does fall damage based on velocity 
    /// </summary>
    /// <param name="fallVel">Velocity value from MoveType</param>
    /// <returns>The damage that was dealt</returns>
    public virtual int DoFallDamage(ushort fallVel)
    {
        var fallDmg = Math.Min(MaxHp, (int)(MaxHp * ((fallVel - 8600) / 15000f)));
        var multiplier = CalculateWithBonuses(0d, UnitAttribute.FallDamageMul) / 100d;
        var minHpLeft = MaxHp / 20; //5% of hp 
        var maxDmgLeft = Hp - minHpLeft; // Max damage one can take 

        fallDmg = (int)(fallDmg + (fallDmg * multiplier));

        if (fallVel >= 32000)
        {
            ReduceCurrentHp(this, Hp); // This is instant death so should be first
            // This will also kill anybody riding this if this is a mount
        }
        else
        {
            if (fallDmg < maxDmgLeft)
            {
                ReduceCurrentHp(this, fallDmg); //If you can take the hit without reaching 5% hp left take it
            }
            else
            {
                var duration = 500 * (fallDmg / minHpLeft);

                var buff = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.FallStun);
                var casterObj = new SkillCasterUnit(ObjId);
                Buffs.AddBuff(new Buff(this, this, casterObj, buff, null, DateTime.UtcNow), 0, duration);

                if (Hp > minHpLeft)
                    ReduceCurrentHp(this, maxDmgLeft); // Leaves you at 5% hp no matter what
            }
        }

        BroadcastPacket(new SCEnvDamagePacket(EnvSource.Falling, ObjId, (uint)fallDmg), true);
        //SendPacket(new SCEnvDamagePacket(EnvSource.Falling, ObjId, (uint)fallDmg));
        // TODO: Maybe adjust formula & need to detect water landing?
        return fallDmg;
    }

    /// <summary>
    /// Set the faction of the owner
    /// </summary>
    /// <param name="factionId"></param>
    public void SetFaction(FactionsEnum factionId)
    {
        // Keep origin faction data temporarily for arena players
        OriginFaction = Faction;

        if (this is Character player)
        {
            // change the faction for the character
            player.OriginFactionName = player.FactionName;
        }

        Logger.Info($"SetFaction: npc={TemplateId}:{ObjId}, factionId={factionId}");

        if (Faction.Id == factionId)
        {
            Logger.Info($"SetFaction: faction has already been established factionId={factionId}");
        }
        else
        {
            BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, Faction?.Id ?? 0, factionId, false), true);
            Faction = FactionManager.Instance.GetFaction(factionId);
        }

        // TODO added for quest Id=2486
        if (this is not Npc npc) { return; }

        // Npc attacks the character
        var characters = WorldManager.GetAround<Character>(npc, 5.0f);
        foreach (var character in characters.Where(CanAttack))
        {
            Logger.Info($"SetFaction: npc={TemplateId}:{ObjId} attack the character={character.Name}:{character.TemplateId}:{character.ObjId}");
            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, character, 1);
            npc.Ai.OnAggroTargetChanged();
            //npc.Ai.GoToCombat();
        }
    }

    public virtual SkillResult UseSkill(uint skillId, IUnit target)
    {
        var skill = new Skill(SkillManager.Instance.GetSkillTemplate(skillId));

        var caster = SkillCaster.GetByType(SkillCasterType.Unit);
        caster.ObjId = ObjId;

        var sct = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
        sct.ObjId = target.ObjId;

        return skill.Use(this, caster, sct, null, true, out _);
    }

    public static void ModelPosture(PacketStream stream, Unit unit, uint animActionId, bool activateAnimation)
    {
        var npc = unit as Npc;

        stream.Write((byte)unit.ModelPostureType);
        stream.Write(unit.HasLootLeft); // isLooted

        switch (unit.ModelPostureType)
        {
            case ModelPostureType.HouseState: // build
                stream.Write((byte)0xF); // flags Byte (2 - door, 6 - window)
                break;
            case ModelPostureType.ActorModelState: // npc
                // Logger.Debug($"Using AnimActionId={animActionId} for NPC TemplateId: {npc?.TemplateId}, ObjId:{npc?.ObjId}");
                stream.Write(animActionId); // Animation override
                stream.Write(activateAnimation); // activate
                break;
            case ModelPostureType.FarmfieldState:
                stream.Write(0u); // type(id)
                stream.Write(0f); // growRate
                stream.Write(0); // randomSeed
                stream.Write(false); // isWithered
                //stream.Write(false); // isHarvested
                break;
            case ModelPostureType.TurretState: // slave
                stream.Write(0f); // pitch
                stream.Write(0f); // yaw
                break;
        }
    }

    public virtual void Regenerate()
    {
        // Do nothing
    }

    public WeaponWieldKind GetWeaponWieldKind()
    {
        var item = Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
        if (item != null && item.Template is WeaponTemplate weapon)
        {
            var slotId = (EquipmentItemSlotType)weapon.HoldableTemplate.SlotTypeId;
            if (slotId == EquipmentItemSlotType.TwoHanded)
                return WeaponWieldKind.TwoHanded;
            else if (slotId == EquipmentItemSlotType.OneHanded || slotId == EquipmentItemSlotType.Mainhand)
            {
                var item2 = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                if (item2 != null && item2.Template is WeaponTemplate weapon2)
                {
                    var slotId2 = (EquipmentItemSlotType)weapon2.HoldableTemplate.SlotTypeId;
                    if (slotId2 == EquipmentItemSlotType.OneHanded || slotId2 == EquipmentItemSlotType.Offhand)
                        return WeaponWieldKind.DuelWielded;
                    else
                        return WeaponWieldKind.OneHanded;
                }
                else
                    return WeaponWieldKind.OneHanded;
            }
        }

        return WeaponWieldKind.None;
    }

    public void UpdateGearBonuses(Item itemAdded, Item itemRemoved)
    {
        // We use index 1 for gear bonuses. Will make this a constant later, or do it properly. Right now the expected behavior is to have key == buff id, which doesn't work when you have items.
        Bonuses[1] = new List<Bonus>();

        foreach (var item in Equipment.Items)
        {
            if (item is not EquipItem ei)
                continue;

            // Mods on the gear Itself
            foreach (var template in ItemManager.Instance.GetUnitModifiers(item.TemplateId))
                AddBonus(1, new Bonus { Template = template, Value = template.Value });

            // Mods from equipped Gems
            foreach (var gem in ei.GemIds)
            foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
                AddBonus(1, new Bonus { Template = template, Value = template.Value });
        }

        // Apply Equipment Effects
        ApplyEquipEffects(itemAdded, itemRemoved);

        // Compute gear buff
        ApplyWeaponWieldBuff();
        ApplyArmorGradeBuff(itemAdded, itemRemoved);
        ApplyEquipItemSetBonuses();
    }

    private void ApplyWeaponWieldBuff()
    {
        Buffs.RemoveBuff((uint)BuffConstants.EquipDualwield);
        Buffs.RemoveBuff((uint)BuffConstants.EquipShield);
        Buffs.RemoveBuff((uint)BuffConstants.EquipTwoHanded);

        BuffTemplate buffTemplate = null;
        switch (GetWeaponWieldKind())
        {
            case WeaponWieldKind.None:
            case WeaponWieldKind.OneHanded:
                var item = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                if (item != null && item.Template is WeaponTemplate weapon)
                {
                    var slotId = (EquipmentItemSlotType)weapon.HoldableTemplate.SlotTypeId;
                    if (slotId == EquipmentItemSlotType.Shield)
                        buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EquipShield);
                }
                break;
            case WeaponWieldKind.TwoHanded:
                buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EquipTwoHanded);
                break;
            case WeaponWieldKind.DuelWielded:
                buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.EquipDualwield);
                break;
        }

        if (buffTemplate != null)
        {
            var effect = new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.UtcNow);
            Buffs.AddBuff(effect);
        }
    }

    private void ApplyEquipItemSetBonuses()
    {
        var setNumPieces = new Dictionary<uint, int>();
        var itemLevels = new Dictionary<uint, uint>();
        foreach (var item in Equipment.Items)
        {
            if (item.Template is EquipItemTemplate template)
            {
                var equipItemSetId = template.EquipItemSetId;
                if (template.EquipItemSetId == 0)
                    continue;

                if (!setNumPieces.ContainsKey(equipItemSetId))
                {
                    setNumPieces.Add(equipItemSetId, (1));
                    itemLevels.Add(equipItemSetId, (uint)item.Template.Level);
                }
                else
                {
                    setNumPieces[equipItemSetId]++;
                    if (item.Template.Level < itemLevels[equipItemSetId])
                        itemLevels[equipItemSetId] = (uint)item.Template.Level;
                }
            }
        }

        var appliedBuffs = new HashSet<uint>();
        foreach (var setCount in setNumPieces)
        {
            var equipItemSet = ItemManager.Instance.GetEquippedItemSet(setCount.Key);
            foreach (var bonus in equipItemSet.Bonuses)
            {
                if (setCount.Value >= bonus.NumPieces)
                {
                    if (bonus.BuffId != 0)
                    {
                        if (Buffs.CheckBuff(bonus.BuffId))
                        {
                            appliedBuffs.Add(bonus.BuffId);
                            continue;
                        }
                        var buffTemplate = SkillManager.Instance.GetBuffTemplate(bonus.BuffId);

                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.UtcNow)
                            {
                                AbLevel = (ushort)itemLevels[setCount.Key]
                            };
                        Buffs.AddBuff(newEffect);
                        appliedBuffs.Add(bonus.BuffId);
                    }
                    if (bonus.ItemProcId != 0 && Procs != null)
                    {
                        Procs.AddProc(bonus.ItemProcId);
                    }
                }
                else //This needs to be revised? Will we ever remove more than 1 item at a time?
                {
                    if (bonus.BuffId != 0 && Buffs.CheckBuff(bonus.BuffId) && !appliedBuffs.Contains(bonus.BuffId))
                        Buffs.RemoveBuff(bonus.BuffId);
                    if (bonus.ItemProcId != 0 && Procs != null)
                        Procs.RemoveProc(bonus.ItemProcId);
                }
            }
        }
    }

    private void ApplyArmorGradeBuff(Item itemAdded, Item itemRemoved)
    {
        if ((itemAdded != null || itemRemoved != null) && (!(itemAdded is Armor) && !(itemRemoved is Armor)))
            return;

        // Clear any existing armor grade buffs
        Buffs.RemoveBuffs((uint)BuffConstants.ArmorBuffTag, 10);

        // Get armor pieces by kind
        var armorPieces = new Dictionary<ArmorType, List<Armor>>();
        foreach (var item in Equipment.Items)
        {
            if (!(item is Armor armor))
                continue;

            if (!(item.Template is ArmorTemplate armorTemplate))
                continue;

            if (armorTemplate.SlotTemplate.SlotTypeId == (ulong)EquipmentItemSlotType.Back)
                continue;

            if (!armorPieces.ContainsKey((ArmorType)armorTemplate.KindTemplate.TypeId))
                armorPieces.Add((ArmorType)armorTemplate.KindTemplate.TypeId, new List<Armor>());
            armorPieces[(ArmorType)armorTemplate.KindTemplate.TypeId].Add(armor);
        }

        if (armorPieces.Count == 0)
            return;
        // Get kind with most pieces
        var piecesOfKind = armorPieces.First();
        foreach (var piecesByKind in armorPieces)
        {
            if (piecesByKind.Value.Count > piecesOfKind.Value.Count) piecesOfKind = piecesByKind;
        }

        var piecesToAccountForBuff = piecesOfKind.Value;

        if (piecesToAccountForBuff.Count < 4)
            return;

        var finalArmorTemplate = piecesToAccountForBuff.First().Template as ArmorTemplate;
        if (finalArmorTemplate == null)
            return;

        if (piecesToAccountForBuff.Count == 7)
        {
            BuffTemplate templ = null;
            switch ((ArmorType)finalArmorTemplate.WearableTemplate.TypeId)
            {
                case ArmorType.Cloth:
                    templ = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Cloth7P);
                    break;
                case ArmorType.Leather:
                    templ = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Leather7P);
                    break;
                case ArmorType.Metal:
                    templ = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Plate7P);
                    break;
            }

            if (templ != null)
                Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(), templ, null, DateTime.UtcNow));
        }
        else
        {
            BuffTemplate templ = null;
            switch ((ArmorType)finalArmorTemplate.WearableTemplate.TypeId)
            {
                case ArmorType.Cloth:
                    templ = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Cloth4P);
                    break;
                case ArmorType.Leather:
                    templ = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Leather4P);
                    break;
                case ArmorType.Metal:
                    templ = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Plate4P);
                    break;
            }

            if (templ != null)
                Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(), templ, null, DateTime.UtcNow));
        }

        // Get only pieces >= arcane
        var piecesAboveArcane = piecesToAccountForBuff.Where(p => p.Grade >= (int)ItemGrade.Arcane).ToList();
        if (piecesAboveArcane.Count < 4)
            return;

        var totalLevel = piecesAboveArcane.Sum(a => a.Template.Level);

        // This const was calculated by hand, it might make no sense.
        var abLevel = totalLevel * 0.40670554f;
        var gradeBuffAbLevel = (abLevel * abLevel) / 15 + 30;
        var lowestGrade = piecesAboveArcane.Min(a => a.Grade);

        // Apply buff 
        if (piecesAboveArcane.First().Template is ArmorTemplate armorTemp)
        {
            var type = armorTemp.WearableTemplate.TypeId;
            var armorGradeBuff =
                ItemManager.Instance.GetArmorGradeBuff((ArmorType)type, (ItemGrade)lowestGrade);
            var buffTemplate = SkillManager.Instance.GetBuffTemplate(armorGradeBuff.BuffId);

            var newEffect =
                new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                {
                    AbLevel = (ushort)gradeBuffAbLevel
                };

            Buffs.AddBuff(newEffect);
        }
    }

    private void ApplyEquipEffects(Item itemAdded, Item itemRemoved)
    {
        if (itemRemoved != null)
        {
            // Static Item Buffs
            var itemRemovedBuff = ItemGameData.Instance.GetItemBuff(itemRemoved.TemplateId, itemRemoved.Grade);
            if (itemRemovedBuff == null)
                itemRemovedBuff = SkillManager.Instance.GetBuffTemplate(itemRemoved.Template?.BuffId ?? 0);
            if (itemRemovedBuff != null) // remove previous buff
            {
                if (Buffs.CheckBuff(itemRemovedBuff.Id))
                {
                    Buffs.RemoveBuff(itemRemovedBuff.Id);
                }
            }

            // Charged Item Buffs
            if ((itemRemoved.Template is EquipItemTemplate equipItemTemplate) &&
                (equipItemTemplate.RechargeBuffId > 0) &&
                Buffs.CheckBuff(equipItemTemplate.RechargeBuffId))
                Buffs.RemoveBuff(equipItemTemplate.RechargeBuffId);
        }

        if (itemAdded != null)
        {
            // Static Buffs
            var itemAddedBuff = ItemGameData.Instance.GetItemBuff(itemAdded.TemplateId, itemAdded.Grade);
            if (itemAddedBuff == null)
                itemAddedBuff = SkillManager.Instance.GetBuffTemplate(itemAdded.Template.BuffId);
            if (itemAddedBuff != null) // add buff from equipped item
            {
                var newEffect =
                    new Buff(this, this, new SkillCasterUnit(), itemAddedBuff, null, DateTime.UtcNow)
                    {
                        AbLevel = (ushort)itemAdded.Template.Level
                    };

                Buffs.AddBuff(newEffect);
            }

            // Charged Item Buffs
            if ((itemAdded is EquipItem equipItem) && (equipItem.Template is EquipItemTemplate equipItemTemplate) &&
                (equipItemTemplate.RechargeBuffId > 0))
            {
                var addChargeBuff = false;
                var checkExpireTime = equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack)
                    ? equipItem.UnpackTime
                    : equipItem.ChargeStartTime;
                checkExpireTime = checkExpireTime.AddMinutes(equipItemTemplate.ChargeLifetime);

                // Check against timer
                if ((equipItemTemplate.ChargeLifetime > 0) && (checkExpireTime > DateTime.UtcNow))
                    addChargeBuff = true;

                // Check against charge counter
                if ((equipItemTemplate.ChargeCount > 0) && (equipItem.ChargeCount > 0))
                    addChargeBuff = true;

                // If this item is Bind on unwrap, don't start the buff if it's not unwrapped
                if (equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) && (equipItem.HasFlag(ItemFlag.Unpacked) == false))
                    addChargeBuff = false;

                if (addChargeBuff)
                {
                    var itemAddedChargedBuff = SkillManager.Instance.GetBuffTemplate(equipItemTemplate.RechargeBuffId);
                    var newEffect =
                        new Buff(this, this, new SkillCasterUnit(), itemAddedChargedBuff, null, DateTime.UtcNow)
                        {
                            AbLevel = (ushort)itemAdded.Template.Level
                        };
                    Buffs.AddBuff(newEffect);
                }
            }
            
            // Unit_Modifiers from items
            
        }

        if (itemAdded == null && itemRemoved == null) // This is the first load check to apply buffs for equipped items. 
        {
            Buffs.RemoveBuffs((uint)BuffConstants.EquipmentBuffTag, 20);
            foreach (var item in Equipment.Items)
            {
                // Static Buffs
                if (item.Template.BuffId != 0)
                {
                    var buffTemplate = ItemGameData.Instance.GetItemBuff(item?.TemplateId ?? 0, item?.Grade ?? 0);
                    if (buffTemplate == null)
                        buffTemplate = SkillManager.Instance.GetBuffTemplate(item?.Template.BuffId ?? 0);
                    var newEffect =
                        new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                        {
                            AbLevel = (ushort)item.Template.Level
                        };

                    Buffs.AddBuff(newEffect);
                }

                // Charged Item Buffs
                if ((item is EquipItem equipItem) && (equipItem.Template is EquipItemTemplate equipItemTemplate) &&
                    (equipItemTemplate.RechargeBuffId > 0))
                {
                    var addChargeBuff = false;
                    var checkExpireTime = equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack)
                        ? equipItem.UnpackTime
                        : equipItem.ChargeStartTime;
                    checkExpireTime = checkExpireTime.AddMinutes(equipItemTemplate.ChargeLifetime);

                    // Check against timer
                    if ((equipItemTemplate.ChargeLifetime > 0) && (checkExpireTime > DateTime.UtcNow))
                        addChargeBuff = true;

                    // Check against charge counter
                    if ((equipItemTemplate.ChargeCount > 0) && (equipItem.ChargeCount > 0))
                        addChargeBuff = true;

                    // If this item is Bind on unwrap, don't start the buff if it's not unwrapped
                    if (equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) && (equipItem.HasFlag(ItemFlag.Unpacked) == false))
                        addChargeBuff = false;

                    if (addChargeBuff)
                    {
                        var itemAddedChargedBuff = SkillManager.Instance.GetBuffTemplate(equipItemTemplate.RechargeBuffId);
                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(), itemAddedChargedBuff, null, DateTime.UtcNow)
                            {
                                AbLevel = (ushort)item.Template.Level
                            };
                        Buffs.AddBuff(newEffect);
                    }
                }
            }
        }
    }

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        // We switched zone keys, we need to do some checks
        var lastZone = ZoneManager.Instance.GetZoneByKey(lastZoneKey);
        var newZone = ZoneManager.Instance.GetZoneByKey(newZoneKey);
        var lastZoneGroupId = (short)(lastZone?.GroupId ?? 0);
        var newZoneGroupId = (short)(newZone?.GroupId ?? 0);
        if (lastZoneGroupId == newZoneGroupId)
            return;

        // Handle Zone Buffs
        if (lastZone != null)
        {
            // Remove the old zone buff if needed
            var lastZoneGroup = ZoneManager.Instance.GetZoneGroupById(lastZone.GroupId);
            if ((lastZoneGroup != null) && (lastZoneGroup.BuffId != 0))
            {
                // Remove the applied buff from last zoneGroup
                Buffs.RemoveBuff(lastZoneGroup.BuffId);
            }
        }
        if (newZone != null)
        {
            // Apply the new zone buff if needed
            var newZoneGroup = ZoneManager.Instance.GetZoneGroupById(newZone.GroupId);
            if ((newZoneGroup != null) && (newZoneGroup.BuffId != 0))
            {
                // Add buff from new zoneGroup
                var buffTemplate = SkillManager.Instance.GetBuffTemplate(newZoneGroup.BuffId);
                if (buffTemplate != null)
                {
                    var casterObj = new SkillCasterUnit(ObjId);
                    var newZoneBuff = new Buff(this, this, casterObj, buffTemplate, null, DateTime.UtcNow);
                    Buffs.AddBuff(newZoneBuff);
                }
            }
        }
        
        // Ok, we actually changed zone groups, we'll have to do some chat channel stuff
        if (this is Character player)
        {
            if (lastZoneGroupId != 0)
                ChatManager.Instance.GetZoneChat(lastZoneKey).LeaveChannel(player);

            if (newZoneGroupId != 0)
                ChatManager.Instance.GetZoneChat(Transform.ZoneId).JoinChannel(player);
        }
    }
}

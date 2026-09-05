using System.Collections.Concurrent;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Gimmicks;
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
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;
using static AAEmu.Game.Models.Game.Units.Buffs;

namespace AAEmu.Game.Models.Game.Units;

public class Unit : BaseUnit, IUnit
{
    public virtual UnitTypeFlag TypeFlag { get => UnitTypeFlag.None; }
    public virtual BaseUnitType BaseUnitType { get; set; } = BaseUnitType.Invalid;

    public virtual UnitEvents Events { get; }
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

    /// <summary>
    /// Signed world selector carried by UnitState. A value of -1 selects the client's current local world.
    /// This is a server-world selector, not the transform's world-template id.
    /// </summary>
    public sbyte UnitStateWorldId { get; set; } = -1;

    /// <summary>
    /// Signed UnitState region selector. A value of -1 asks the client to derive the region from position.
    /// </summary>
    public sbyte UnitStateRegionId { get; set; } = -1;

    /// <summary>Whether this unit belongs to the cross-server global world.</summary>
    public bool IsInGlobalWorld { get; set; }

    /// <summary>
    /// makes actor selection fall back to the unit template or character race and gender.
    /// </summary>
    public int UnitStateType { get; set; }

    /// <summary>
    /// value; federated identity providers may supply a nonzero value with the matching world selector.
    /// </summary>
    public long UnitStateIdentityValue { get; set; }

    /// <summary>UnitState heirLevel. Character values are derived from cumulative heir experience.</summary>
    public virtual byte HeirLevel { get; set; }

    /// <summary>UnitState vechicleDyeing (the client's spelling) — signed packed colour applied to a slave.</summary>
    public int VehicleDyeing { get; set; }

    /// <summary>UnitState isTempFaction — set while a unit carries an event or quest faction.</summary>
    public bool IsTempFaction { get; set; }

    /// <summary>Signed attack-faction flags copied by UnitState and later updates.</summary>
    public sbyte AttackFactionFlags { get; set; }

    /// <summary>Selected appellation stamp id; distinct from the character's active appellation/title id.</summary>
    public uint AppellationStampId { get; set; }

    /// <summary>Whether other clients may open this unit's equipment information.</summary>
    public bool IsEquipmentPublic { get; set; }

    /// <summary>Client presentation state set by SCUnitOffline and included in UnitState snapshots.</summary>
    public bool IsOffline { get; set; }

    public uint DuelStateObjectId { get; set; }

    /// <summary>Conditional action tail used only by WZUnitState.</summary>
    public UnitStateAction UnitStateAction { get; } = new();

    /// <summary>Current UnitState target object, or zero when this state snapshot has no target.</summary>
    public uint UnitStateTargetObjId { get; set; }

    public UnitStateOptionalData UnitStateOptionalData { get; set; }

    public int Hp { get; set; }

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
    protected List<int> HpTriggerPointsPercent { get; set; } = [];

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
    public int Mpp
    {
        get
        {
            if (MaxMp <= 0)
                return 0;
            return Math.Clamp((int)Math.Ceiling(Mp * 100f / MaxMp), 0, 100);
        }
    }

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
    public UnitCustomModelParams ModelParams { get; set; } = new();
    public byte ActiveWeapon { get; set; }
    public bool IdleStatus { get; set; }
    public bool ForceAttack { get; set; }
    public bool Invisible { get; set; }
    public uint OwnerId { get; set; }
    public SkillTask SkillTask { get; set; }
    public SkillTask AutoAttackTask { get; set; }
    public DateTime GlobalCooldown { get; set; }
    public bool IsGlobalCooldownDone => GlobalCooldown > DateTime.UtcNow;
    public object GcdLock { get; set; }
    public DateTime SkillLastUsed { get; set; }
    public PlotState ActivePlotState { get; set; }
    public Dictionary<uint, List<Bonus>> Bonuses { get; set; }
    public Dictionary<uint, List<DynamicBonus>> DynamicBonuses { get; set; }
    public UnitCooldowns Cooldowns { get; set; }
    public virtual Expedition Expedition { get; set; }

    /// <summary>
    /// Accumulated combat resources by combat_resources id — the combo-point style pools an ability builds up
    /// (광란, 착취, 근성 …). Plot flow gates on these through PlotCondition kind 19 and skill_effects'
    /// target_combat_resource_id.
    /// </summary>
    public Dictionary<int, int> CombatResources { get; } = [];

    /// <summary>
    /// When each resource next loses <c>combat_resources.peace_recovery_amount</c> /
    /// <c>combat_recovery_amount</c>. A resource with no entry is idle: it is either empty or its
    /// <c>recovery_cycle</c> is 0 (기쁨 / 슬픔 never drain).
    /// </summary>
    private readonly Dictionary<int, DateTime> _combatResourceDecayAt = [];

    public int GetCombatResource(int combatResourceId) => CombatResources.GetValueOrDefault(combatResourceId, 0);

    /// <summary>
    /// Adds to a combat resource, clamped to that resource's own ceiling from combat_resources.max and never
    /// below zero. Returns the amount actually held afterwards.
    /// </summary>
    /// <param name="combatResourceId">combat_resources id.</param>
    /// <param name="amount">Signed delta; negative spends.</param>
    /// <param name="resetDecayTimer">
    /// <c>combat_resource_effects.reset_remain_time</c>. The shipped rows set it on every builder, which is
    /// what makes the short cycles workable: 증오 drains its whole pool every 5s, so without restarting the
    /// timer on each gain the window to spend it would depend on when the last tick happened to land.
    /// </param>
    public int AddCombatResource(int combatResourceId, int amount, bool resetDecayTimer = true)
    {
        var max = CombatResourceGameData.Instance.GetMax(combatResourceId);
        var current = GetCombatResource(combatResourceId);

        // An unknown id has no ceiling to clamp against; keep it non-negative rather than inventing one.
        var updated = max > 0
            ? Math.Clamp(current + amount, 0, max)
            : Math.Max(0, current + amount);

        CombatResources[combatResourceId] = updated;
        ArmCombatResourceDecay(combatResourceId, updated, resetDecayTimer);
        SyncCombatResourceBuff(combatResourceId, updated);
        return updated;
    }

    /// <summary>
    /// Seeds <c>combat_resources.default_point</c> for resources this unit owns.
    /// Death's brand starts at 6 and Pleasure's joy/sorrow at 5; other classes must
    /// not receive those pools or their bar buffs.
    /// </summary>
    public void InitializeCombatResources()
    {
        IReadOnlySet<int> owned = null;
        if (this is Character character)
            owned = CombatResourceGameData.Instance.ResourceIdsForAbilities(
                (int)character.Ability1, (int)character.Ability2, (int)character.Ability3);

        foreach (var id in CombatResourceSeedRules.HeldToDrop(CombatResources.Keys, owned))
            DropCombatResource(id);

        foreach (var resource in CombatResourceGameData.Instance.WithDefaultPoint)
        {
            if (owned != null && !CombatResourceSeedRules.ShouldSeed(resource.Id, owned))
                continue;
            CombatResources[resource.Id] = resource.Max > 0
                ? Math.Min(resource.DefaultPoint, resource.Max)
                : resource.DefaultPoint;
            ArmCombatResourceDecay(resource.Id, CombatResources[resource.Id], restart: true);
            SyncCombatResourceBuff(resource.Id, CombatResources[resource.Id]);
        }
    }

    private void DropCombatResource(int resourceId)
    {
        CombatResources.Remove(resourceId);
        _combatResourceDecayAt.Remove(resourceId);
        SyncCombatResourceBuff(resourceId, 0);
        var resource = CombatResourceGameData.Instance.Get(resourceId);
        if (resource != null)
            BroadcastCombatResource(resource, 0, updateTimeMs: 0);
    }

    /// <summary>
    /// Drains pools whose cycle has elapsed. Driven from <see cref="OnActiveRegionTick"/>, i.e. about once a
    /// second for units in a region a player is watching.
    /// </summary>
    private void CombatResourceTick(TimeSpan delta)
    {
        if (_combatResourceDecayAt.Count == 0)
            return;

        var now = DateTime.UtcNow;
        List<int> due = null;
        foreach (var (resourceId, at) in _combatResourceDecayAt)
        {
            if (at <= now)
                (due ??= []).Add(resourceId);
        }

        if (due == null)
            return;

        foreach (var resourceId in due)
        {
            var resource = CombatResourceGameData.Instance.Get(resourceId);
            if (resource == null)
            {
                _combatResourceDecayAt.Remove(resourceId);
                continue;
            }

            var step = resource.RecoveryAmountFor(IsInBattle);
            if (step == 0)
            {
                _combatResourceDecayAt.Remove(resourceId);
                continue;
            }

            // Decay must not restart its own timer from the change — that would make a pool that drains in
            // several steps re-arm on every step and never reach the next one on schedule.
            var before = GetCombatResource(resourceId);
            var after = AddCombatResource(resourceId, step, resetDecayTimer: false);
            if (after == before)
            {
                _combatResourceDecayAt.Remove(resourceId);
                continue;
            }

            BroadcastCombatResource(resource, after, updateTimeMs: 0);

            if (after > 0)
                _combatResourceDecayAt[resourceId] = now.AddMilliseconds(resource.RecoveryCycle);
            else
                _combatResourceDecayAt.Remove(resourceId);
        }
    }

    /// <summary>Starts, restarts or clears a resource's decay timer for its current amount.</summary>
    private void ArmCombatResourceDecay(int combatResourceId, int amount, bool restart)
    {
        var resource = CombatResourceGameData.Instance.Get(combatResourceId);
        // recovery_cycle 0 means the pool is not on a timer at all, not "drain every tick".
        if (resource == null || resource.RecoveryCycle <= 0 || amount <= 0)
        {
            _combatResourceDecayAt.Remove(combatResourceId);
            return;
        }

        if (restart || !_combatResourceDecayAt.ContainsKey(combatResourceId))
            _combatResourceDecayAt[combatResourceId] = DateTime.UtcNow.AddMilliseconds(resource.RecoveryCycle);
    }

    /// <summary>
    /// Holds or drops the resource's bar buff. The client draws the pip UI off this buff, so without it the
    /// point packets arrive against a bar that was never shown.
    /// </summary>
    private void SyncCombatResourceBuff(int combatResourceId, int amount)
    {
        var resource = CombatResourceGameData.Instance.Get(combatResourceId);
        if (resource is not { BuffId: > 0 } || Buffs == null)
            return;

        var shouldHold = resource.ShouldHoldBuff(amount);
        var holds = Buffs.CheckBuff(resource.BuffId);
        if (shouldHold == holds)
            return;

        if (shouldHold)
        {
            // An id with no template would NRE inside Buffs.AddBuff. Every shipped bar buff resolves, but
            // this runs on data we do not control.
            if (SkillManager.Instance.GetBuffTemplate(resource.BuffId) == null)
            {
                Logger.Warn("Combat resource {0} names buff {1}, which is not loaded", resource.Id, resource.BuffId);
                return;
            }

            Buffs.AddBuff(resource.BuffId, this);
            return;
        }

        // 기쁨 (26) and 슬픔 (27) share buff 29976. Dropping it for one while the other still qualifies would
        // take the bar away from both.
        foreach (var other in CombatResourceGameData.Instance.All)
        {
            if (other.Id != combatResourceId && other.BuffId == resource.BuffId &&
                other.ShouldHoldBuff(GetCombatResource(other.Id)))
                return;
        }

        Buffs.RemoveBuff(resource.BuffId);
    }

    /// <summary>
    /// Pushes a resource total to whoever combat_resources.resouece_send_type_id says should see it
    /// (1 Self, 2 Broadcast).
    /// </summary>
    public void BroadcastCombatResource(CombatResource resource, int amount, int updateTimeMs)
    {
        if (resource == null)
            return;

        var packet = new SCCombatResourcePointPacket(ObjId, resource.Id, (ulong)Math.Max(0, amount), updateTimeMs);
        if (resource.SendTypeId == 2)
            BroadcastPacket(packet, true);
        else
            (this as Char.Character)?.SendPacket(packet);
    }

    /// <summary>Sends every non-empty pool, so a freshly entered client starts with a bar that matches the server.</summary>
    public void SendAllCombatResources()
    {
        foreach (var resource in CombatResourceGameData.Instance.All)
        {
            var amount = GetCombatResource(resource.Id);
            if (amount != 0)
                BroadcastCombatResource(resource, amount, updateTimeMs: 0);
        }
    }

    private bool _isInBattle;

    public bool IsInBattle
    {
        get => _isInBattle;
        set => SetBattleState(value, broadcastToClients: true, relayToZone: true);
    }

    /// <summary>Mirror a Zone-authored battle transition without reflecting it back over WZ.</summary>
    internal void SetBattleStateFromZone(bool value) =>
        SetBattleState(value, broadcastToClients: false, relayToZone: false);

    private void SetBattleState(bool value, bool broadcastToClients, bool relayToZone)
    {
        if (value == _isInBattle)
            return;

        _isInBattle = value;
        if (!_isInBattle)
        {
            if (broadcastToClients)
                BroadcastPacket(new SCCombatClearedPacket(ObjId), true);
            if (relayToZone && WorldIntegration.ZoneAuthority)
                WorldIntegration.RelayCombatClearedToZone?.Invoke(ObjId);
        }
        else if (relayToZone && WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayCombatEngagedToZone?.Invoke(ObjId);
            if (this is Character ch && ch.CurrentTarget != null)
                WorldIntegration.RelayRequestCombatUnitsToZone?.Invoke(ObjId, ch.CurrentTarget.ObjId);
        }
    }

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

    public ConcurrentDictionary<uint, Aggro> AggroTable { get; } = [];

    public Unit()
    {
        Events = new UnitEvents();
        GcdLock = new object();
        Bonuses = [];
        DynamicBonuses = [];
        IsInBattle = false;
        Equipment = new EquipmentContainer(0, SlotType.Equipment, false, this);
        ChargeLock = new object();
        Cooldowns = new UnitCooldowns();
        CharacterTagging = new Tagging(this); //Adding because Tagging works differently than Aggro
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

        // Characters handle underwater/breath in Character.SetPosition.
        // Avoid double-updating IsUnderWater (and packet spam) for players.
        if (this is Character)
            return;

        var worldDrownThreshold = WorldManager.Instance.GetWorld(Transform.InstanceId)?.Template.OceanLevel - 2f ?? 98f;
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

        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Hp > 0 ? Mp : 0), true);

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
                    if (oldHpP > triggerValue && newHpP <= triggerValue)
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
                    if (oldHpP < triggerValue && newHpP >= triggerValue)
                    {
                        DoHpChangeTrigger(triggerValue, false, oldHpValue, newHpValue);
                        break;
                    }
                }
            }
        }

        // Only the transition into death counts. Calling this after a revive setup (old=0,new>0)
        // or re-publishing on an already-dead unit (old=0,new=0) must not re-run DoDie/SCUnitDeath.
        if (Hp > 0 || newHpValue > 0)
            return;
        if (oldHpValue <= 0)
            return;

        if (attackerBase is Unit attackerUnit)
        {
            attackerUnit.Events.OnKill(attackerUnit, new OnKillArgs { Target = attackerUnit });

            var world = WorldManager.Instance.GetWorld(Transform.InstanceId);
            if (Transform.WorldId > 0)
            {
                if (world.DungeonInstance is not null)
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
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp), true);
    }

    public virtual void DoDie(BaseUnit killer, KillReason killReason)
    {
        InterruptSkills();

        IsInBattle = false;

        Events.OnDeath(this, new OnDeathArgs { Killer = (Unit)killer, Victim = this });
        ParentWorld.Events.OnUnitKilled(ParentWorld, new OnUnitKilledArgs { Killer = (Unit)killer, Victim = this });
        ((Unit)killer).Events.OnKill(this, new OnKillArgs { Killer = (Unit)killer, Victim = this });

        Buffs.RemoveEffectsOnDeath();
        killer.BroadcastPacket(new SCUnitDeathPacket(ObjId, killReason, (Unit)killer), true);
        if (killer == this)
        {
            switch (this)
            {
                case Mate mate:
                    DespawnMate(WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId));
                    break;
                case Character character:
                    DespawnMate(character);
                    break;
            }
            return;
        }

        // Generate the loot for this Npc
        LootingContainer.GenerateLoot(killer);

        // Cleanup targeting and aggro packets
        if (CurrentTarget != null)
        {
            var aggroNpcId = this is Npc ? ObjId : killer is Npc killerNpc ? killerNpc.ObjId : 0;
            if (aggroNpcId != 0)
                killer.SendPacketToPlayers([this, killer], new SCAiAggroPacket(aggroNpcId));

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
            if (killer is Character targetingCharacter)
                targetingCharacter.SendPacket(new SCTargetChangedPacket(targetingCharacter.ObjId, 0));
            else
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

    private static void DespawnMate(Character character)
    {
        // if we died sitting on a horse
        if (character.Hp > 0) { return; }

        var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id).ToList();
        foreach (var mate in mateList)
        {
            character.Mates.DespawnMate(mate.TlId);
        }
    }

    public void StopAutoSkill(Unit unit)
    {
        if (unit.AutoAttackTask is null || unit is not Character character)
            return;

        var aaSkill = character.AutoAttackTask.Skill;
        var skillId = aaSkill?.Template?.Id ?? 0;
        // Abort in-flight basic-attack Use/Apply so its SC packets cannot land after a
        // stop_autoattack cast has already sent SCSkillStarted (cancels cast anim).
        if (aaSkill != null)
            aaSkill.Cancelled = true;
        character.AutoAttackTask.Cancelled = true;
        character.AutoAttackTask.Cancel();
        character.AutoAttackTask = null;
        character.IsAutoAttack = false;
        // Clears client action_slot.auto_attack when the unit flag was latched.
        if (skillId != 0)
            character.BroadcastPacket(new SCSkillStoppedPacket(character.ObjId, skillId), true);
    }

    public void StartAutoSkill(Skill skill)
    {
        if (this is not Character character)
            return;

        // Replace a dead/cancelled task — old StopAutoSkill only set Cancelled and left
        // AutoAttackTask non-null, so StartAutoSkill silently no-op'd forever.
        if (character.AutoAttackTask != null)
        {
            if (!character.AutoAttackTask.Cancelled &&
                character.AutoAttackTask.Skill?.Template?.Id == skill.Template?.Id)
                return; // already running this attack
            StopAutoSkill(character);
        }

        var newTask = new UseAutoAttackSkillTask(skill, character);
        character.IsAutoAttack = true;
        character.AutoAttackTask = newTask;
        var attackDelayTimes = SkillManager.GetAttackDelay(skill.Template, character);

        TaskManager.Instance.Schedule(character.AutoAttackTask, TimeSpan.FromMilliseconds(attackDelayTimes),
            TimeSpan.FromMilliseconds(attackDelayTimes), -1);
        Logger.Info("StartAutoSkill char={0} skill={1} delayMs={2:F0}",
            character.ObjId, skill.Template?.Id, attackDelayTimes);
    }

    public void SetInvisible(bool value)
    {
        Invisible = value;
        BroadcastPacket(new SCUnitInvisiblePacket(ObjId, Invisible), true);
    }

    /// <summary>
    /// Notifies nearby clients of a GM-mode marker change via the dedicated GmModeChanged
    /// opcode. Currently known meaning: mode 6 toggles the GM icon shown next to the
    /// character's name; value 1 enables it, 0 disables it.
    ///
    /// Also persists the value into UnitStateOptionalData.GmModeValues so that a full
    /// UnitState sync (e.g. sent to an observer who only now starts seeing this unit)
    /// carries the current GM-mode state too. The dedicated opcode alone is fire-and-forget
    /// and only reaches clients that were already observing this unit at call time.
    /// </summary>
    public void SendGmModeChanged(int mode, byte value)
    {
        if (mode >= 0 && mode < (UnitStateOptionalData ??= new UnitStateOptionalData()).GmModeValues.Length)
            UnitStateOptionalData.GmModeValues[mode] = unchecked((sbyte)value);

        BroadcastPacket(new SCUnitGmModeChangedPacket(ObjId, mode, value), true);
    }

    /// <summary>
    /// Reads back a value previously set via <see cref="SendGmModeChanged"/>. This is the
    /// single source of truth for GM-mode marker state — it lives on the Unit itself and is
    /// naturally reset on reconnect (fresh Unit, null UnitStateOptionalData), unlike a
    /// separate lookup keyed by ObjId, which can go stale when an ObjId is reused across
    /// sessions.
    /// </summary>
    public bool GetGmModeValue(int mode)
    {
        if (UnitStateOptionalData is null || mode < 0 || mode >= UnitStateOptionalData.GmModeValues.Length)
            return false;
        return UnitStateOptionalData.GmModeValues[mode] != 0;
    }

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
    public void SetCriminalState(bool criminalState, BaseUnit attackedTarget)
    {
        if (criminalState)
        {
            // Retribution (Felon) is for a player attacking players — not NPCs, ships, or mounts,
            // and only a player can be the criminal. A hull's own plots put crew debuffs on its
            // riders (anchor lowered → 21950 "move speed 0" on the crew); that is not a crime.
            if (this is not Character)
                return;
            if (attackedTarget is Npc and not Portal)
                return;
            if (attackedTarget is Slave or Mate)
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
        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayForceAttackToZone?.Invoke(ObjId, ForceAttack);
    }

    public override void AddBonus(uint bonusIndex, Bonus bonus)
    {
        var bonuses = Bonuses.TryGetValue(bonusIndex, out var bonusList) ? bonusList : [];
        bonuses.Add(bonus);
        Bonuses[bonusIndex] = bonuses;
    }

    public override void RemoveBonus(uint bonusIndex, UnitAttribute attribute)
    {
        if (!Bonuses.TryGetValue(bonusIndex, out var bonuses))
        {
            return;
        }

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

    public override void AddDynamicBonus(uint bonusIndex, DynamicBonus bonus)
    {
        var bonuses = DynamicBonuses.TryGetValue(bonusIndex, out var bonusList) ? bonusList : [];
        bonuses.Add(bonus);
        DynamicBonuses[bonusIndex] = bonuses;
    }

    public override void RemoveDynamicBonus(uint bonusIndex, UnitAttribute attribute)
    {
        if (!DynamicBonuses.TryGetValue(bonusIndex, out var bonuses))
        {
            return;
        }

        foreach (var bonus in new List<DynamicBonus>(bonuses))
        {
            if (bonus.Template != null && bonus.Template.Attribute == attribute)
            {
                bonuses.Remove(bonus);
            }
        }

        if (bonuses.Count == 0)
        {
            DynamicBonuses.Remove(bonusIndex);
        }
    }

    public List<DynamicBonus> GetDynamicBonuses(UnitAttribute attribute)
    {
        var result = new List<DynamicBonus>();
        if (DynamicBonuses == null)
        {
            return result;
        }
        foreach (var bonuses in new List<List<DynamicBonus>>(DynamicBonuses.Values))
        {
            foreach (var bonus in new List<DynamicBonus>(bonuses))
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
        // Order: static flat -> dynamic flat -> static percent -> dynamic percent.
        // Dynamic bonuses are evaluated on the fly from their source buff so that time-varying
        // modifiers (LinearFunc dynamic_unit_modifiers) reflect the current elapsed time rather
        // than a value snapshotted at buff Start.
        var bonuses = GetBonuses(attr);
        var dynamicBonuses = GetDynamicBonuses(attr);

        // Static flat values
        foreach (var bonus in bonuses)
        {
            if (bonus.Template.ModifierType != UnitModifierType.Value)
                continue;
            value += bonus.Value;
        }

        // Dynamic flat values
        foreach (var dynamicBonus in dynamicBonuses)
        {
            if (dynamicBonus.Template.ModifierType != UnitModifierType.Value)
                continue;
            if (dynamicBonus.Evaluate(out var dynValue))
                value += dynValue;
        }

        // Static percent values
        foreach (var bonus in bonuses)
        {
            if (bonus.Template.ModifierType != UnitModifierType.Percent)
                continue;
            value += value * bonus.Value / 100f;
        }

        // Dynamic percent values
        foreach (var dynamicBonus in dynamicBonuses)
        {
            if (dynamicBonus.Template.ModifierType != UnitModifierType.Percent)
                continue;
            if (dynamicBonus.Evaluate(out var dynValue))
                value += value * dynValue / 100f;
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

    public virtual bool IsLooted { get; set; }
    public virtual ModelPostureType ModelPostureType { get => ModelPostureType.None; }
    public Gimmick Gimmick { get; set; }

    /// <summary>
    /// Tagging works differently to Aggro and has its own system 
    /// </summary>
    public Tagging CharacterTagging { get; set; }
    public virtual void OnSkillEnd(Skill skill)
    {

    }

    private const float LegacyFallVelocityUnitsPerMeterPerSecond = 1000f;
    private const float FallDamageStartSpeed = 8.6f;
    private const float LethalFallSpeed = 32f;
    private const float FallDamageRampSpeed = 15f;

    /// <summary>Compatibility entry point for the legacy client movement velocity field.</summary>
    public int DoFallDamage(ushort fallVel) =>
        DoFallDamage(fallVel / LegacyFallVelocityUnitsPerMeterPerSecond);

    /// <summary>Applies fall damage from the Zone-authored positive impact speed in metres per second.</summary>
    /// <returns>The HP actually removed after immunity, immortality and damage absorption.</returns>
    public virtual int DoFallDamage(float impactSpeed)
    {
        if (!float.IsFinite(impactSpeed) || impactSpeed <= FallDamageStartSpeed || Hp <= 0 || MaxHp <= 0)
            return 0;

        if (Buffs.HasEffectsMatchingCondition(buff => buff.Template.FallDamageImmune))
            return 0;

        var fallDamageMultiplier = 1d + CalculateWithBonuses(0d, UnitAttribute.FallDamageMul) / 100d;
        var computedDamage = (int)(MaxHp * ((impactSpeed - FallDamageStartSpeed) / FallDamageRampSpeed)
                                       * fallDamageMultiplier);
        computedDamage = Math.Clamp(computedDamage, 0, MaxHp);

        var hpBefore = Hp;
        var lethalImpact = impactSpeed >= LethalFallSpeed;
        var fallImmortality = Buffs.HasEffectsMatchingCondition(
            buff => buff.Template.FallDamageImmortality);

        if (lethalImpact && !fallImmortality)
        {
            ReduceCurrentHp(this, Hp);
        }
        else
        {
            var minimumHp = lethalImpact && fallImmortality ? 1 : Math.Max(1, MaxHp / 20);
            var maximumDamage = Math.Max(0, Hp - minimumHp);
            var appliedRequest = Math.Min(computedDamage, maximumDamage);

            if (computedDamage >= maximumDamage && computedDamage > 0)
            {
                var stunHpStep = Math.Max(1, MaxHp / 20);
                var duration = 500 * Math.Max(1, computedDamage / stunHpStep);
                var buff = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.FallStun);
                if (buff != null)
                {
                    var casterObj = new SkillCasterUnit(ObjId);
                    Buffs.AddBuff(new Buff(this, this, casterObj, buff, null, DateTime.UtcNow), 0, duration);
                }
            }

            if (appliedRequest > 0)
                ReduceCurrentHp(this, appliedRequest);
        }

        var appliedDamage = Math.Max(0, hpBefore - Hp);
        if (appliedDamage > 0)
            BroadcastPacket(new SCEnvDamagePacket(EnvSource.Falling, ObjId, (uint)appliedDamage), true);
        return appliedDamage;
    }

    /// <summary>
    /// Set the faction of the owner
    /// </summary>
    /// <param name="factionId"></param>
    public void SetFaction(FactionsEnum factionId)
    {
        // Keep origin faction data temporarily for arena players
        OriginFaction = Faction;
        var player = this as Character;

        // change the faction for the character
        player?.OriginFactionName = player.FactionName;

        Logger.Info($"SetFaction: npc={TemplateId}:{ObjId}, factionId={factionId}");

        if (Faction.Id == factionId)
        {
            Logger.Info($"SetFaction: faction has already been established factionId={factionId}");
        }
        else
        {
            var oldFactionId = Faction?.Id ?? 0;
            // BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, Faction?.Id ?? 0, factionId, false), true);
            Faction = FactionManager.Instance.GetFaction(factionId);
            BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, oldFactionId, Faction.Id, false), true);
            if (WorldIntegration.ZoneAuthority)
                WorldIntegration.RelayUnitFactionChangedToZone?.Invoke(ObjId, (int)oldFactionId, (int)Faction.Id, false);
        }

        // A faction flip makes nearby players valid targets (quest 2486). Seed the aggro table so
        // kill credit and loot rights resolve; the Zone decides whether the NPC actually engages.
        if (this is not Npc npc)
            return;

        foreach (var character in WorldManager.GetAround<Character>(npc, 5.0f).Where(CanAttack))
        {
            Logger.Info($"SetFaction: npc={TemplateId}:{ObjId} is now hostile to {character.Name}:{character.ObjId}");
            npc.AddUnitAggro(AggroKind.Damage, character, 1);
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
        UnitModelPostureSerializer.Write(stream, unit, animActionId, activateAnimation);
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
        // Capture before gear-index clear so MaxHp still reflects the previous loadout.
        var oldMaxHp = MaxHp;
        var oldMaxMp = MaxMp;
        var wasFullHp = Hp >= oldMaxHp && oldMaxHp > 0;
        var wasFullMp = Mp >= oldMaxMp && oldMaxMp > 0;

        Bonuses[GearBonusesIndex] = [];

        foreach (var item in Equipment.Items)
        {
            if (item is not EquipItem ei)
                continue;

            // Mods on the gear Itself
            foreach (var template in ItemManager.Instance.GetUnitModifiers(item.TemplateId))
                AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });

            // Mods from equipped Gems
            foreach (var gem in ei.GemIds)
                foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
                    AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });

            // Synthesis effects. The item stores the effect's group; what it is worth follows from
            // the grade the piece is at and how far into it the piece has come, so a line grows both
            // when a grade is gained and as the bar toward the next one fills.
            foreach (var groupId in ei.UsedRndAttrGroupIds)
            {
                var modifier = ItemEnchantGameData.Instance.GetRndAttrModifier(groupId, ei.Grade, ei.EvolvingExp);
                // Not "> 0": the reducing lines are the negative ones, and they count too.
                if (modifier == null || modifier.Value == 0)
                    continue;

                var template = new BonusTemplate
                {
                    Attribute = (UnitAttribute)modifier.Attribute,
                    ModifierType = (UnitModifierType)modifier.ModifierType,
                    Value = modifier.Value
                };
                AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });
                Logger.Debug("Synthesis bonus: item {0} group {1} -> {2} {3} {4}",
                    ei.Id, groupId, template.Attribute, template.ModifierType, template.Value);
            }
        }

        // Apply Equipment Effects
        ApplyEquipEffects(itemAdded, itemRemoved);

        // Compute gear buff
        ApplyWeaponWieldBuff();
        ApplyArmorGradeBuff(itemAdded, itemRemoved);
        ApplyEquipItemSetBonuses();

        // Gear that raises MaxHp/MaxMp left current points on the old ceiling (pet armor: 8194/9423).
        if (wasFullHp)
            Hp = MaxHp;
        else
            Hp = Math.Min(Hp, MaxHp);
        if (wasFullMp)
            Mp = MaxMp;
        else
            Mp = Math.Min(Mp, MaxMp);

        // The client counts a worn piece's synthesis effects only while its slot is marked as
        // carrying them. That mark otherwise rides along with the unit state, which is not sent
        // again for a piece that gains, loses or swaps an effect while being worn - so it is
        // republished here, where every such change already passes through.
        if (this is Character { Connection: not null } wearer && wearer.ObjId != 0)
            wearer.SendPacket(new SCUnitEquipmentsRndAttrUnitModifierActivatedPacket(wearer));
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

                if (!setNumPieces.TryGetValue(equipItemSetId, out var value))
                {
                    setNumPieces.Add(equipItemSetId, 1);
                    itemLevels.Add(equipItemSetId, (uint)item.Template.Level);
                }
                else
                {
                    setNumPieces[equipItemSetId] = ++value;
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
                                AbLevel = itemLevels[setCount.Key]
                            };
                        Buffs.AddBuff(newEffect);
                        appliedBuffs.Add(bonus.BuffId);
                    }
                    if (bonus.ItemProcId != 0)
                    {
                        Procs.AddProc(bonus.ItemProcId);
                    }
                }
                else //This needs to be revised? Will we ever remove more than 1 item at a time?
                {
                    if (bonus.BuffId != 0 && Buffs.CheckBuff(bonus.BuffId) && !appliedBuffs.Contains(bonus.BuffId))
                        Buffs.RemoveBuff(bonus.BuffId);
                    if (bonus.ItemProcId != 0)
                        Procs.RemoveProc(bonus.ItemProcId);
                }
            }
        }
    }

    private void ApplyArmorGradeBuff(Item itemAdded, Item itemRemoved)
    {
        if ((itemAdded != null || itemRemoved != null) && itemAdded is not Items.Armor && itemRemoved is not Items.Armor)
            return;

        // Clear any existing armor grade buffs
        Buffs.RemoveBuffs((uint)BuffConstants.ArmorBuffTag, 10);

        // Get armor pieces by kind
        var armorPieces = new Dictionary<ArmorType, List<Armor>>();
        foreach (var item in Equipment.Items)
        {
            if (item is not Armor armor)
                continue;

            if (item.Template is not ArmorTemplate armorTemplate)
                continue;

            if (armorTemplate.SlotTemplate.SlotTypeId == (ulong)EquipmentItemSlotType.Back)
                continue;

            if (!armorPieces.ContainsKey((ArmorType)armorTemplate.KindTemplate.TypeId))
                armorPieces.Add((ArmorType)armorTemplate.KindTemplate.TypeId, []);
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
            BuffTemplate buffTemplate = null;
            switch ((ArmorType)finalArmorTemplate.WearableTemplate.TypeId)
            {
                case ArmorType.Cloth:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Cloth7P);
                    break;
                case ArmorType.Leather:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Leather7P);
                    break;
                case ArmorType.Metal:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Plate7P);
                    break;
            }

            if (buffTemplate != null)
                Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow));
        }
        else
        {
            BuffTemplate buffTemplate = null;
            switch ((ArmorType)finalArmorTemplate.WearableTemplate.TypeId)
            {
                case ArmorType.Cloth:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Cloth4P);
                    break;
                case ArmorType.Leather:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Leather4P);
                    break;
                case ArmorType.Metal:
                    buffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Plate4P);
                    break;
            }

            if (buffTemplate != null)
                Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow));
        }

        // Get only pieces >= arcane
        var piecesAboveArcane = piecesToAccountForBuff.Where(p => p.Grade >= (int)ItemGrade.Arcane).ToList();
        if (piecesAboveArcane.Count < 4)
            return;

        var totalLevel = piecesAboveArcane.Sum(a => a.Template.Level);

        // This const was calculated by hand, it might make no sense.
        var abLevel = totalLevel * 0.40670554f;
        var gradeBuffAbLevel = abLevel * abLevel / 15 + 30;
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
                    AbLevel = (uint)gradeBuffAbLevel
                };

            Buffs.AddBuff(newEffect);
        }
    }

    private void ApplyEquipEffects(Item itemAdded, Item itemRemoved)
    {
        if (itemRemoved != null)
        {
            // Static Item Buffs
            var itemRemovedBuff = ItemGameData.Instance.GetItemBuff(itemRemoved.TemplateId, itemRemoved.Grade) ??
                                  SkillManager.Instance.GetBuffTemplate(itemRemoved.Template?.BuffId ?? 0);
            if (itemRemovedBuff != null) // remove previous buff
            {
                if (Buffs.CheckBuff(itemRemovedBuff.Id))
                {
                    Buffs.RemoveBuff(itemRemovedBuff.Id);
                }
            }

            // Charged Item Buffs
            if (itemRemoved.Template is EquipItemTemplate equipItemTemplate &&
                equipItemTemplate.RechargeBuffId > 0 &&
                Buffs.CheckBuff(equipItemTemplate.RechargeBuffId))
                Buffs.RemoveBuff(equipItemTemplate.RechargeBuffId);
        }

        if (itemAdded != null)
        {
            // Static Buffs
            var itemAddedBuff = ItemGameData.Instance.GetItemBuff(itemAdded.TemplateId, itemAdded.Grade) ?? SkillManager.Instance.GetBuffTemplate(itemAdded.Template.BuffId);
            if (itemAddedBuff != null) // add buff from equipped item
            {
                var newEffect =
                    new Buff(this, this, new SkillCasterUnit(), itemAddedBuff, null, DateTime.UtcNow)
                    {
                        AbLevel = (uint)itemAdded.Template.Level
                    };

                Buffs.AddBuff(newEffect);
            }

            // Charged Item Buffs
            if (itemAdded is EquipItem equipItem && equipItem.Template is EquipItemTemplate equipItemTemplate &&
                equipItemTemplate.RechargeBuffId > 0)
            {
                var addChargeBuff = false;
                var checkExpireTime = equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack)
                    ? equipItem.UnpackTime
                    : equipItem.ChargeStartTime;
                checkExpireTime = checkExpireTime.AddMinutes(equipItemTemplate.ChargeLifetime);

                // Check against timer
                if (equipItemTemplate.ChargeLifetime > 0 && checkExpireTime > DateTime.UtcNow)
                    addChargeBuff = true;

                // Check against charge counter
                if (equipItemTemplate.ChargeCount > 0 && equipItem.ChargeCount > 0)
                    addChargeBuff = true;

                // If this item is Bind on unwrap, don't start the buff if it's not unwrapped
                if (equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) && equipItem.HasFlag(ItemFlag.Unpacked) == false)
                    addChargeBuff = false;

                if (addChargeBuff)
                {
                    var itemAddedChargedBuff = SkillManager.Instance.GetBuffTemplate(equipItemTemplate.RechargeBuffId);
                    var newEffect =
                        new Buff(this, this, new SkillCasterUnit(), itemAddedChargedBuff, null, DateTime.UtcNow)
                        {
                            AbLevel = (uint)itemAdded.Template.Level
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
                    var buffTemplate = ItemGameData.Instance.GetItemBuff(item?.TemplateId ?? 0, item?.Grade ?? 0) ??
                                       SkillManager.Instance.GetBuffTemplate(item?.Template.BuffId ?? 0);
                    var newEffect =
                        new Buff(this, this, new SkillCasterUnit(), buffTemplate, null, DateTime.UtcNow)
                        {
                            AbLevel = (uint)item.Template.Level
                        };

                    Buffs.AddBuff(newEffect);
                }

                // Charged Item Buffs
                if (item is EquipItem equipItem && equipItem.Template is EquipItemTemplate equipItemTemplate &&
                    equipItemTemplate.RechargeBuffId > 0)
                {
                    var addChargeBuff = false;
                    var checkExpireTime = equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack)
                        ? equipItem.UnpackTime
                        : equipItem.ChargeStartTime;
                    checkExpireTime = checkExpireTime.AddMinutes(equipItemTemplate.ChargeLifetime);

                    // Check against timer
                    if (equipItemTemplate.ChargeLifetime > 0 && checkExpireTime > DateTime.UtcNow)
                        addChargeBuff = true;

                    // Check against charge counter
                    if (equipItemTemplate.ChargeCount > 0 && equipItem.ChargeCount > 0)
                        addChargeBuff = true;

                    // If this item is Bind on unwrap, don't start the buff if it's not unwrapped
                    if (equipItemTemplate.BindType.HasFlag(ItemBindType.BindOnUnpack) && equipItem.HasFlag(ItemFlag.Unpacked) == false)
                        addChargeBuff = false;

                    if (addChargeBuff)
                    {
                        var itemAddedChargedBuff = SkillManager.Instance.GetBuffTemplate(equipItemTemplate.RechargeBuffId);
                        var newEffect =
                            new Buff(this, this, new SkillCasterUnit(), itemAddedChargedBuff, null, DateTime.UtcNow)
                            {
                                AbLevel = (uint)item.Template.Level
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
            if (lastZoneGroup != null && lastZoneGroup.BuffId != 0)
            {
                // Remove the applied buff from last zoneGroup
                Buffs.RemoveBuff(lastZoneGroup.BuffId);
            }
        }
        if (newZone != null)
        {
            // Apply the new zone buff if needed
            var newZoneGroup = ZoneManager.Instance.GetZoneGroupById(newZone.GroupId);
            if (newZoneGroup != null && newZoneGroup.BuffId != 0)
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
    }

    private readonly Dictionary<uint, int> _triggerCounts = new();

    public void IncrementTriggerCount(uint buffId)
    {
        if (!_triggerCounts.TryAdd(buffId, 1))
        {
            _triggerCounts[buffId]++;
        }
    }

    public void DecrementTriggerCount(uint buffId)
    {
        if (_triggerCounts.ContainsKey(buffId) && _triggerCounts[buffId] > 0)
        {
            _triggerCounts[buffId]--;
        }
    }

    public int GetTriggerCount(uint buffId)
    {
        return _triggerCounts.GetValueOrDefault(buffId, 0);
    }

    /// <summary>
    /// Handle is still in combat related things
    /// </summary>
    /// <param name="delta"></param>
    protected virtual void CombatTick(TimeSpan delta)
    {
        // TODO: Make it so you can also become out of combat if you are not on any aggro lists
        if (IsInBattle && LastCombatActivity.AddSeconds(WorldManager.DefaultCombatTimeout) < DateTime.UtcNow)
        {
            IsInBattle = false;
        }
    }

    /// <summary>
    /// Call regeneration function of the unit
    /// </summary>
    /// <param name="delta"></param>
    protected virtual void RegenTick(TimeSpan delta)
    {
        // Do nothing
    }

    /// <summary>
    /// Tick called for Units in active player regions about once per second
    /// </summary>
    /// <param name="delta"></param>
    public virtual void OnActiveRegionTick(TimeSpan delta)
    {
        CombatTick(delta);
        RegenTick(delta);
        CombatResourceTick(delta);
    }

    /// <summary>
    /// Adds aggro
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="unit"></param>
    /// <param name="amount"></param>
    /// <returns>Returns true if it's initial aggro</returns>
    public bool AddUnitAggro(AggroKind kind, Unit unit, int amount)
    {
        //var player = unit as Character; // TODO player.Region становится равным null | player.Region becomes null
        var player = unit as Character;
        var npc = this as Npc;
        var isNewAggro = false;
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
            return false;
        }

        // check target buff tags
        if ((unit.Buffs?.CheckBuffTag((uint)TagsEnum.NoFight) ?? false) || (unit.Buffs?.CheckBuffTag((uint)TagsEnum.Returning) ?? false))
        {
            ClearAggroOfUnit(unit);
            return false;
        }


        //Add Tagging if it was damage aggro
        if (kind == AggroKind.Damage)
            CharacterTagging.AddTagger(unit, amount);

        amount = (int)(amount * (unit.AggroMul / 100.0f));
        amount = (int)(amount * (IncomingAggroMul / 100.0f));

        if (AggroTable.TryGetValue(unit.ObjId, out var aggro))
        {
            aggro.AddAggro(kind, amount);
            isNewAggro = true;
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
            if (npc != null)
            {
                if (npc.Template.EngageCombatGiveQuestId > 0 && player is not null)
                {
                    if (!player.Quests.IsQuestComplete(npc.Template.EngageCombatGiveQuestId) &&
                        !player.Quests.HasQuest(npc.Template.EngageCombatGiveQuestId))
                        player.Quests.AddQuest(npc.Template.EngageCombatGiveQuestId);
                }
            }

            // Send initial hit packet as well
            unit.SendPacketToPlayers([this, unit], new SCCombatFirstHitPacket(this.ObjId, unit.ObjId, 0));
        }

        if (player == null)
            return isNewAggro;

        if (aggro.TotalAggro > 0 && !IsDead && Hp > 0 && !player.IsInAggroListOf.ContainsKey(this.ObjId))
        {
            player.IsInAggroListOf.Add(this.ObjId, this);
        }
        //player?.Quests.OnAggro(this);
        // инициируем событие
        //Task.Run(() => QuestManager.Instance.DoOnAggroEvents(player, this));
        if (npc != null)
        {
            QuestManager.Instance.DoOnAggroEvents(player, npc);
        }
        return isNewAggro;
    }

    public void ClearAggroOfUnit(Unit unit)
    {
        if (unit is null)
            return;

        if (unit is Character targetPlayer)
        {
            targetPlayer.IsInAggroListOf.Remove(ObjId);
            // Also remove from assault lists if both are players
            if (this is Character thisPlayer)
            {
                thisPlayer.AssaultOn.Remove(targetPlayer.Id);
                targetPlayer.AssaultedBy.Remove(thisPlayer.Id);
            }
        }

        // var player = unit as Character;
        // player?.SendMessage($"ClearAggroOfUnit {player.Name} for {this.ObjId}");

        var lastAggroCount = AggroTable.Count;
        if (lastAggroCount <= 0)
        {
            return;
        }
        if (AggroTable.TryRemove(unit.ObjId, out _))
        {
            unit.Events.OnHealed -= OnAbuserHealed;
            unit.Events.OnDeath -= OnAbuserDied;
        }
        else
        {
            Logger.Warn($"Failed to remove unit[{unit.ObjId}] aggro from NPC[{ObjId}]");
        }

        if (AggroTable.Count != lastAggroCount)
            (this as Npc)?.CheckIfEmptyAggroToReturn(unit);
    }

    public void OnAbuserHealed(object sender, OnHealedArgs args)
    {
        AddUnitAggro(AggroKind.Heal, args.Healer, args.HealAmount);
    }

    public void OnAbuserDied(object sender, OnDeathArgs args)
    {
        ClearAggroOfUnit(args.Victim);
    }

    /// <summary>
    /// address damage, heal, and direct/script aggro respectively; any non-zero selector replaces
    /// </summary>
    public void ApplyAggroReset(
        int damageSelector,
        int healSelector,
        int directSelector,
        int applyValue)
    {
        if (damageSelector == 0 && healSelector == 0 && directSelector == 0 && applyValue == 0)
        {
            ClearAllAggro();
            return;
        }

        foreach (var aggro in AggroTable.Values.ToArray())
        {
            aggro.ApplyComponentSelectors(
                damageSelector != 0,
                healSelector != 0,
                directSelector != 0,
                applyValue);

            // IsEmpty/Count as a combat-state invariant and has no dormant-entry state, so the
            // equivalent zero-hostility result must remove the row and its reverse index here.
            if (aggro.TotalAggro <= 0)
                ClearAggroOfUnit(aggro.Owner);
        }
    }

    /// <summary>
    /// Replaces this unit's aggro table with an exact component snapshot of
    /// without applying aggro multipliers, first-hit events, quest triggers, or heal scaling.
    /// </summary>
    public void CopyAggroFrom(Unit source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(this, source))
            return;

        var sourceEntries = source.AggroTable.Values
            .Select(aggro => (aggro.Owner, Components: aggro.GetComponents()))
            .ToArray();

        ClearAllAggro();

        foreach (var (owner, components) in sourceEntries)
        {
            // A zero/negative entry is not a live hostility relationship and must not leave a
            // stale event subscription or Character.IsInAggroListOf reverse entry.
            if (components.Total <= 0)
                continue;

            var copiedAggro = new Aggro(owner, components);
            if (!AggroTable.TryAdd(owner.ObjId, copiedAggro))
                continue;

            owner.Events.OnHealed += OnAbuserHealed;
            owner.Events.OnDeath += OnAbuserDied;

            if (owner is Character player && !IsDead && Hp > 0 &&
                !player.IsInAggroListOf.ContainsKey(ObjId))
            {
                player.IsInAggroListOf.Add(ObjId, this);
            }
        }
    }

    public virtual void ClearAllAggro()
    {
        // Adding for tagging
        CharacterTagging.ClearAllTaggers();

        // Remove while the table is still populated so ClearAggroOfUnit can also maintain event
        // subscriptions and Character.IsInAggroListOf. Clearing first leaves that reverse index
        // stale and keeps characters stuck in combat.
        foreach (var aggro in AggroTable.Values.ToArray())
            ClearAggroOfUnit(aggro.Owner);
    }
}

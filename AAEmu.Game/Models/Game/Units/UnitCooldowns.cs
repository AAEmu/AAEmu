using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Skills;

using NLog;

namespace AAEmu.Game.Models.Game.Units;

public class UnitCooldowns
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<uint, CooldownState> _cooldowns = new();

    /// <summary>
    /// Cooldowns armed by <c>skills.cooldown_tag_id</c> / <c>second_cooldown_tag_id</c> /
    /// <c>third_cooldown_tag_id</c>. Kept apart from <see cref="_cooldowns"/> because a tag id and a
    /// skill id are different key spaces that overlap numerically (skill 3317 and tag 3317 both exist).
    /// </summary>
    private readonly ConcurrentDictionary<uint, CooldownState> _tagCooldowns = new();

    /// <summary>
    /// Charge pools for the 26 skills that declare <c>charge_count</c>. Guarded by
    /// <see cref="_chargeLock"/> rather than being a lock-free structure: a pool is a read-modify-write
    /// pair, and charge skills are cast a handful of times a minute, not on a hot path.
    /// </summary>
    private readonly Dictionary<uint, SkillChargeRules.ChargeState> _charges = [];
    private readonly object _chargeLock = new();

    private readonly record struct CooldownState(DateTime EndTime, uint Duration);

    public readonly record struct CooldownSnapshot(uint SkillId, uint Duration, uint Remaining);

    /// <summary>
    /// The slack a running cooldown keeps after it has nominally expired. The client starts its own
    /// countdown when the cast is sent, so the server's clock is always a little ahead of the press.
    /// </summary>
    private static readonly TimeSpan ExpirySlack = TimeSpan.FromMilliseconds(250);

    public void AddCooldown(uint skillId, uint duration)
        => AddCooldown(skillId, duration, null);

    /// <summary>
    /// Arms the skill id and, when given, every cooldown tag the skill carries, so sibling skills that
    /// share a tag share the cooldown. 295 of the 534 ability skills carry <c>cooldown_tag_id</c>.
    /// </summary>
    public void AddCooldown(uint skillId, uint duration, IReadOnlyList<int> tagIds)
    {
        var endTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(duration);
        var state = new CooldownState(endTime, duration);
        _cooldowns.AddOrUpdate(skillId, state, (_, _) => state);

        if (tagIds == null)
            return;

        foreach (var tagId in tagIds)
        {
            if (tagId > 0)
                _tagCooldowns.AddOrUpdate((uint)tagId, state, (_, _) => state);
        }
    }

    public bool CheckCooldown(uint skillId)
        => IsActive(_cooldowns, skillId);

    /// <summary>
    /// True while any of the cooldown tags the skill shares with its siblings is still running.
    /// </summary>
    public bool CheckTagCooldown(IReadOnlyList<int> tagIds)
    {
        if (tagIds == null)
            return false;

        foreach (var tagId in tagIds)
        {
            if (tagId > 0 && IsActive(_tagCooldowns, (uint)tagId))
                return true;
        }

        return false;
    }

    /// <summary>True while the skill's own cooldown or any of its cooldown tags is still running.</summary>
    public bool CheckCooldown(uint skillId, IReadOnlyList<int> tagIds)
        => CheckCooldown(skillId) || CheckTagCooldown(tagIds);

    private static bool IsActive(ConcurrentDictionary<uint, CooldownState> store, uint key)
    {
        if (!store.TryGetValue(key, out var state))
            return false;

        var timeLeft = state.EndTime - DateTime.UtcNow;

        //Logger.Debug($"CheckCooldown: timeLeft={timeLeft}");

        if (timeLeft > ExpirySlack)
            return true;

        store.TryRemove(key, out _);
        return false;
    }

    public void RemoveCooldown(uint skillId)
    {
        _cooldowns.TryRemove(skillId, out _);
    }

    /// <summary>Remaining time of the longest of the skill's own and tag cooldowns.</summary>
    public TimeSpan GetRemaining(uint skillId, IReadOnlyList<int> tagIds)
    {
        var remaining = GetRemaining(skillId);
        if (tagIds == null)
            return remaining;

        foreach (var tagId in tagIds)
        {
            if (tagId <= 0 || !_tagCooldowns.TryGetValue((uint)tagId, out var state))
                continue;

            var tagRemaining = state.EndTime > DateTime.UtcNow ? state.EndTime - DateTime.UtcNow : TimeSpan.Zero;
            if (tagRemaining > remaining)
                remaining = tagRemaining;
        }

        return remaining;
    }

    public TimeSpan GetRemaining(uint skillId)
    {
        if (!_cooldowns.TryGetValue(skillId, out var state))
            return TimeSpan.Zero;
        return state.EndTime > DateTime.UtcNow ? state.EndTime - DateTime.UtcNow : TimeSpan.Zero;
    }

    /// <summary>
    /// Applies an authored percent-or-flat cooldown reduction to a running cooldown. A negative
    /// authored value extends the remaining time, bounded by the original duration.
    /// </summary>
    /// <remarks>
    /// An entry that has already expired is dropped rather than reduced. Nothing prunes on a timer, so an
    /// entry can sit past its end until something reads it, and a negative reduction applied to that dead
    /// entry would compute a positive remaining time and re-arm a cooldown the client is already showing as
    /// ready — the skill would then be refused. Reducing a live entry is all this is for.
    /// </remarks>
    public void ApplyCooldownReduction(uint skillId, int flatMilliseconds, int percent)
    {
        while (_cooldowns.TryGetValue(skillId, out var state))
        {
            var now = DateTime.UtcNow;
            if (state.EndTime <= now)
            {
                // Compare-and-remove so a concurrent arm of the same skill is not discarded with the
                // expired entry it replaced.
                _cooldowns.TryRemove(new KeyValuePair<uint, CooldownState>(skillId, state));
                return;
            }

            var originalDuration = TimeSpan.FromMilliseconds(state.Duration);
            var remaining = state.EndTime - now;
            var remainingAfterReduction = CooldownReductionRules.CalculateRemaining(
                originalDuration,
                remaining,
                flatMilliseconds,
                percent);
            var updated = state with { EndTime = now + remainingAfterReduction };

            if (_cooldowns.TryUpdate(skillId, updated, state))
                return;
        }
    }

    /// <summary>
    /// Returns the active native cooldown tuple: skill id, original duration, and remaining
    /// duration, all represented as 32-bit wire values.
    /// </summary>
    public IReadOnlyList<CooldownSnapshot> GetActiveSnapshots(int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);

        var now = DateTime.UtcNow;
        var snapshots = new List<CooldownSnapshot>(Math.Min(_cooldowns.Count, maximumCount));
        foreach (var (skillId, state) in _cooldowns.OrderBy(entry => entry.Key))
        {
            if (snapshots.Count >= maximumCount)
                break;

            var remaining = state.EndTime - now;
            if (remaining <= TimeSpan.Zero)
            {
                _cooldowns.TryRemove(skillId, out _);
                continue;
            }

            var remainingMilliseconds = Math.Ceiling(remaining.TotalMilliseconds);
            var wireRemaining = remainingMilliseconds >= uint.MaxValue
                ? uint.MaxValue
                : (uint)remainingMilliseconds;
            snapshots.Add(new CooldownSnapshot(skillId, state.Duration, wireRemaining));
        }

        return snapshots;
    }

    /// <summary>
    /// Charges left on a skill, after granting any that the recharge clock has earned.
    /// </summary>
    public int GetCharges(uint skillId, int maxCharges, int rechargeTime)
    {
        lock (_chargeLock)
            return ReadCharges(skillId, maxCharges, rechargeTime).Current;
    }

    /// <summary>
    /// Spends one charge and returns how many are left. Zero means the pool was already empty and
    /// nothing was spent; that is the caller's cue to gate the cast on the skill's own cooldown instead.
    /// </summary>
    public int ConsumeCharge(uint skillId, int maxCharges, int rechargeTime)
    {
        lock (_chargeLock)
        {
            var state = ReadCharges(skillId, maxCharges, rechargeTime);
            if (state.Current <= 0)
                return 0;

            var after = SkillChargeRules.Consume(state, rechargeTime, DateTime.UtcNow);
            _charges[skillId] = after;
            return after.Current;
        }
    }

    /// <summary>Adds (or removes) charges — special effect 166.</summary>
    public void ChangeChargeCount(uint skillId, int maxCharges, int delta)
    {
        lock (_chargeLock)
        {
            var state = ReadCharges(skillId, maxCharges, 0);
            _charges[skillId] = SkillChargeRules.ChangeCount(state, maxCharges, delta);
        }
    }

    /// <summary>Shifts the running recharge timer — special effect 167.</summary>
    public void ChangeChargeRechargeTime(uint skillId, int maxCharges, int deltaMilliseconds)
    {
        lock (_chargeLock)
        {
            var state = ReadCharges(skillId, maxCharges, 0);
            _charges[skillId] = SkillChargeRules.ChangeRechargeTime(state, deltaMilliseconds, DateTime.UtcNow);
        }
    }

    /// <summary>Restarts the recharge timer with a stated interval — special effect 158.</summary>
    public void RestartChargeRecharge(uint skillId, int maxCharges, int rechargeTime)
    {
        lock (_chargeLock)
        {
            var state = ReadCharges(skillId, maxCharges, 0);
            _charges[skillId] = SkillChargeRules.RestartRecharge(state, rechargeTime, DateTime.UtcNow);
        }
    }

    /// <summary>Sets the pool's ceiling (special effect 166 may name a different count than the skill).</summary>
    public void ResizeChargePool(uint skillId, int maxCharges, int rechargeTime)
    {
        lock (_chargeLock)
            _charges[skillId] = ReadCharges(skillId, maxCharges, rechargeTime);
    }

    private SkillChargeRules.ChargeState ReadCharges(uint skillId, int maxCharges, int rechargeTime)
    {
        if (maxCharges <= 0)
            return SkillChargeRules.Initial(0);

        if (!_charges.TryGetValue(skillId, out var state) || state.Max != maxCharges)
            state = SkillChargeRules.Initial(maxCharges);

        var recharged = SkillChargeRules.Recharge(state, rechargeTime, DateTime.UtcNow, out _);
        if (recharged != state)
            _charges[skillId] = recharged;

        return recharged;
    }
}

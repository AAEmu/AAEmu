using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Per-skill charge pools: how many uses a charge-bearing skill has left, and how long until the next
/// one comes back.
/// </summary>
/// <remarks>
/// The pool sits next to <see cref="UnitCooldowns"/> because a charge skill paces itself with charges
/// instead of with the cooldown: a cast that finds a charge spends it and arms nothing, and only the
/// cast that empties the pool arms the skill's cooldown. <c>skills.charge_count</c> is the ceiling and
/// <c>skills.charge_cooldown_time</c> the recharge interval (38893 빛의 사격: 3 charges, 16000 ms,
/// against a 9000 ms cooldown), which the charge_cooldown (158) and change_charge_cooldown (167)
/// effects override per skill.
/// <para>
/// Entries are created on first use and are never persisted: charges regenerate in minutes, so a relog
/// starts from a full pool, exactly as the client assumes when it draws the pips.
/// </para>
/// </remarks>
public class UnitCharges
{
    private readonly ConcurrentDictionary<uint, ChargeState> _charges = new();

    private sealed record ChargeState(int Max, int Available, uint RechargeMs, DateTime LastCredit)
    {
        public ChargeState Refilled(DateTime now)
        {
            var refill = ChargeSkillRules.Refill(Available, Max, RechargeMs, now - LastCredit);
            return this with
            {
                Available = refill.Available,
                LastCredit = now - refill.Since
            };
        }
    }

    public readonly record struct ChargeSnapshot(uint SkillId, int Available, int Max, uint RechargeMs);

    /// <summary>
    /// The pool as it stands, after crediting the charges that have come back.
    /// </summary>
    /// <param name="skillId">The skill the pool belongs to.</param>
    /// <param name="templateMax">The ceiling its template authors; used when the pool is new.</param>
    /// <param name="templateRechargeMs">The interval its template authors (<c>charge_cooldown_time</c>);
    /// used when the pool is new.</param>
    public ChargeSnapshot GetSnapshot(uint skillId, int templateMax, uint templateRechargeMs, DateTime now)
    {
        var state = Refresh(skillId, templateMax, templateRechargeMs, now);
        return new ChargeSnapshot(skillId, state.Available, state.Max, state.RechargeMs);
    }

    /// <summary>
    /// Spends one charge for a cast.
    /// </summary>
    /// <returns>
    /// False when the pool is empty, which is the caller's signal to arm the normal skill cooldown
    /// instead.
    /// </returns>
    public bool TrySpend(uint skillId, int templateMax, uint templateRechargeMs, DateTime now)
    {
        var state = Refresh(skillId, templateMax, templateRechargeMs, now);
        if (state.Available <= 0)
            return false;

        ChargeState spent;
        do
        {
            spent = state;
            state = state with { Available = state.Available - 1 };
        }
        while (!_charges.TryUpdate(skillId, state, spent));

        return true;
    }

    /// <summary>change_charge_skill_count (166): moves the ceiling, and the pool with it.</summary>
    public ChargeSnapshot AddMax(uint skillId, int delta, int templateMax, uint templateRechargeMs, DateTime now)
    {
        var state = Refresh(skillId, templateMax, templateRechargeMs, now);
        var newMax = ChargeSkillRules.AddedMax(state.Max, delta);
        // A raised ceiling hands out the charge it added; a lowered one drops what no longer fits.
        var newAvailable = Math.Clamp(state.Available + Math.Max(0, delta), 0, newMax);
        Store(skillId, state with { Max = newMax, Available = newAvailable });
        return new ChargeSnapshot(skillId, newAvailable, newMax, state.RechargeMs);
    }

    /// <summary>change_charge_cooldown (167): shifts the recharge interval by a delta in milliseconds.</summary>
    public ChargeSnapshot AddRecharge(uint skillId, int deltaMs, int templateMax, uint templateRechargeMs,
        DateTime now)
    {
        var state = Refresh(skillId, templateMax, templateRechargeMs, now);
        var recharge = ChargeSkillRules.ChangedRecharge(state.RechargeMs, deltaMs);
        Store(skillId, state with { RechargeMs = recharge });
        return new ChargeSnapshot(skillId, state.Available, state.Max, recharge);
    }

    /// <summary>charge_cooldown (158): sets the recharge interval outright.</summary>
    public ChargeSnapshot SetRecharge(uint skillId, uint rechargeMs, int templateMax, uint templateRechargeMs,
        DateTime now)
    {
        var state = Refresh(skillId, templateMax, templateRechargeMs, now);
        Store(skillId, state with { RechargeMs = rechargeMs });
        return new ChargeSnapshot(skillId, state.Available, state.Max, rechargeMs);
    }

    /// <summary>Every pool this unit has touched, for the cooldown packet's charge bucket.</summary>
    /// <remarks>
    /// Each pool is credited first, so the counts are the ones a player would see rather than whatever
    /// the last cast happened to leave behind.
    /// </remarks>
    public IReadOnlyList<ChargeSnapshot> GetSnapshots(int maximumCount, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);

        var snapshots = new List<ChargeSnapshot>(Math.Min(_charges.Count, maximumCount));
        foreach (var skillId in _charges.Keys.Order())
        {
            if (snapshots.Count >= maximumCount)
                break;

            // Refresh only consults its template arguments when it has to create a pool, and every key
            // here already has one, so the numbers it is handed cannot matter.
            var state = Refresh(skillId, 0, 0, now);
            snapshots.Add(new ChargeSnapshot(skillId, state.Available, state.Max, state.RechargeMs));
        }

        return snapshots;
    }

    private ChargeState Refresh(uint skillId, int templateMax, uint templateRechargeMs, DateTime now)
    {
        ChargeState state;
        while (true)
        {
            state = _charges.GetOrAdd(skillId, _ => NewPool(templateMax, templateRechargeMs, now));
            var refreshed = state.Refilled(now);
            if (refreshed.Equals(state) || _charges.TryUpdate(skillId, refreshed, state))
                return refreshed;
        }
    }

    private static ChargeState NewPool(int templateMax, uint templateRechargeMs, DateTime now)
        => new(Math.Max(0, templateMax), Math.Max(0, templateMax), templateRechargeMs, now);

    private void Store(uint skillId, ChargeState state) => _charges[skillId] = state;
}

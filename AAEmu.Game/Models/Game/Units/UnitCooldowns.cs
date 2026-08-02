using System.Collections.Concurrent;

using NLog;

namespace AAEmu.Game.Models.Game.Units;

public class UnitCooldowns
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<uint, CooldownState> _cooldowns = new();

    private readonly record struct CooldownState(DateTime EndTime, uint Duration);

    public readonly record struct CooldownSnapshot(uint SkillId, uint Duration, uint Remaining);

    public void AddCooldown(uint skillId, uint duration)
    {
        var endTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(duration);
        var state = new CooldownState(endTime, duration);
        _cooldowns.AddOrUpdate(skillId, state, (_, _) => state);
    }

    public bool CheckCooldown(uint skillId)
    {
        if (!_cooldowns.TryGetValue(skillId, out var state))
            return false;

        var timeLeft = state.EndTime - DateTime.UtcNow;

        //Logger.Debug($"CheckCooldown: timeLeft={timeLeft}");

        if (timeLeft > TimeSpan.FromMilliseconds(250))
            return true;

        RemoveCooldown(skillId);
        return false;
    }

    public void RemoveCooldown(uint skillId)
    {
        _cooldowns.TryRemove(skillId, out _);
    }

    public TimeSpan GetRemaining(uint skillId)
    {
        if (!_cooldowns.TryGetValue(skillId, out var state))
            return TimeSpan.Zero;
        return state.EndTime > DateTime.UtcNow ? state.EndTime - DateTime.UtcNow : TimeSpan.Zero;
    }

    /// <summary>Shortens a running cooldown; a negative reduction extends it.</summary>
    public void ReduceCooldown(uint skillId, TimeSpan reduction)
    {
        if (!_cooldowns.TryGetValue(skillId, out var state))
            return;

        var adjusted = state.EndTime - reduction;
        if (adjusted <= DateTime.UtcNow)
            RemoveCooldown(skillId);
        else
            _cooldowns[skillId] = state with { EndTime = adjusted };
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
}

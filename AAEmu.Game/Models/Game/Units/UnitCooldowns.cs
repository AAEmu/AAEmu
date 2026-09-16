using System.Collections.Concurrent;

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

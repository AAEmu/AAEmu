using System.Collections.Concurrent;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Tracks the last membership edge reported for a unit in a Zone area.
/// Zone owns the enter/leave decision; the edge memory here only suppresses
/// duplicate edges that carry no state change.
/// </summary>
public sealed class AreaEdgeTracker
{
    /// <summary>Process-wide tracker; the state outlives any one host.</summary>
    public static AreaEdgeTracker Shared { get; } = new();

    private readonly ConcurrentDictionary<AreaEdgeKey, bool> _memberships = new();

    /// <summary>
    /// Keys whose edge work is running right now, mapped to the generation that owns the
    /// slot. A second caller stands down, and a claim that already finished cannot take
    /// the slot away from the claim that holds it.
    /// </summary>
    private readonly Dictionary<AreaEdgeKey, long> _inFlight = [];

    /// <summary>
    /// Generation of the last claim or invalidation stamped on a key. A claim may publish
    /// only while its own generation is still the current one.
    /// </summary>
    private readonly Dictionary<AreaEdgeKey, long> _generations = [];

    /// <summary>Strictly increasing, so a generation is never reused for the same key.</summary>
    private long _sequence;

    private readonly object _gate = new();

    /// <summary>
    /// Returns true only when <paramref name="entering"/> is a state change.
    /// </summary>
    public bool TryTransition(AreaEdgeKey key, bool entering)
    {
        if (entering)
            return _memberships.TryAdd(key, true);
        return _memberships.TryRemove(key, out _);
    }

    /// <summary>
    /// True when the key already holds a membership. Read on its own this changes
    /// nothing.
    /// </summary>
    public bool IsInside(AreaEdgeKey key) => _memberships.ContainsKey(key);

    /// <summary>
    /// Takes exclusive ownership of one key's edge work.
    /// <para>
    /// Returns no claim when the key is already inside or another caller is running its
    /// work, which is what makes a pair of concurrent enter edges for the same doodad
    /// dispatch exactly once. The caller must follow with <see cref="Commit"/> when the
    /// work succeeded or <see cref="Abort"/> when it did not.
    /// </para>
    /// </summary>
    public AreaEdgeClaim? TryBegin(AreaEdgeKey key)
    {
        lock (_gate)
        {
            if (_memberships.ContainsKey(key) || _inFlight.ContainsKey(key))
                return null;

            var claim = new AreaEdgeClaim(key, ++_sequence);
            _generations[key] = claim.Generation;
            _inFlight[key] = claim.Generation;
            return claim;
        }
    }

    /// <summary>
    /// Publishes the membership after the work succeeded.
    /// </summary>
    /// <returns>
    /// False when the claim was already finished, or when a leave, a doodad removal or a
    /// reset invalidated it while the work was running. A claim that lost that race must
    /// not resurrect a membership the newer state already dropped, so nothing is
    /// published and the edge stays open for the next enter.
    /// </returns>
    public bool Commit(AreaEdgeClaim claim) => Finish(claim, publish: true);

    /// <summary>
    /// Releases ownership without publishing a membership, so a later edge retries
    /// the work instead of finding it already done.
    /// </summary>
    public void Abort(AreaEdgeClaim claim) => Finish(claim, publish: false);

    /// <summary>
    /// Releases every doodad's membership for one unit in one area. A leave edge
    /// arrives after the unit has already stepped out, so the membership is
    /// dropped for all owners at once instead of being rediscovered by range.
    /// <para>
    /// Both <paramref name="groupId"/> and <paramref name="areaId"/> are required. The
    /// group alone identifies the KIND of area, not the area, so matching on it would
    /// drop every other area of the same kind that the unit is still inside.
    /// </para>
    /// </summary>
    public int ForgetMembership(uint zoneId, uint unitId, uint groupId, uint areaId) =>
        Invalidate(key => key.ZoneId == zoneId && key.UnitId == unitId
                         && key.GroupId == groupId && key.AreaId == areaId);

    public int ForgetUnit(uint unitId) => Invalidate(key => key.UnitId == unitId);

    public int ForgetArea(uint groupId) => Invalidate(key => key.GroupId == groupId);

    public int ForgetOwner(uint ownerObjId) => Invalidate(key => key.OwnerObjId == ownerObjId);

    /// <summary>
    /// Drops every membership and invalidates any claim still running. Area membership is
    /// session state that the next enter edge rebuilds, so a boot must not inherit it
    /// from a previous run in the same process, and a dispatch left over from before the
    /// reset must not write into it afterwards.
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            foreach (var key in _inFlight.Keys.ToList())
                Stamp(key);
            _inFlight.Clear();
            _memberships.Clear();
        }
    }

    public void Clear() => Reset();

    private bool Finish(AreaEdgeClaim claim, bool publish)
    {
        lock (_gate)
        {
            // The claim must still own the slot: a claim that already finished must not
            // take the slot away from the claim that holds it now.
            if (!_inFlight.TryGetValue(claim.Key, out var owner) || owner != claim.Generation)
                return false;

            _inFlight.Remove(claim.Key);

            var committed = false;
            if (publish &&
                _generations.TryGetValue(claim.Key, out var current) &&
                current == claim.Generation)
            {
                _memberships[claim.Key] = true;
                committed = true;
            }

            Prune(claim.Key);
            return committed;
        }
    }

    private int Invalidate(Func<AreaEdgeKey, bool> match)
    {
        lock (_gate)
        {
            var removed = 0;
            foreach (var key in _memberships.Keys)
            {
                if (match(key) && _memberships.TryRemove(key, out _))
                    removed++;
            }

            // A dispatch already running for a matching key keeps running, but its claim
            // is stamped so the commit it is about to attempt is refused.
            foreach (var key in _inFlight.Keys.ToList())
            {
                if (match(key))
                    Stamp(key);
            }

            return removed;
        }
    }

    private void Stamp(AreaEdgeKey key) => _generations[key] = ++_sequence;

    private void Prune(AreaEdgeKey key)
    {
        if (!_memberships.ContainsKey(key) && !_inFlight.ContainsKey(key))
            _generations.Remove(key);
    }
}

/// <summary>
/// Identifies one unit's membership of one area for one doodad.
/// <para>
/// <see cref="GroupId"/> is the area KIND the edge was reported under and
/// <see cref="AreaId"/> is the individual area within that kind. They are different
/// quantities and a single field cannot hold both: the kind is constant across every
/// area of the family, so a key built from it alone conflates all of them.
/// </para>
/// </summary>
public readonly record struct AreaEdgeKey(uint ZoneId, uint UnitId, uint GroupId, uint AreaId, uint OwnerObjId);

/// <summary>
/// Exclusive ownership of one key's edge work, carrying the generation it was taken at,
/// so a leave, a removal or a reset landing mid-dispatch can refuse the commit.
/// </summary>
public readonly record struct AreaEdgeClaim(AreaEdgeKey Key, long Generation);

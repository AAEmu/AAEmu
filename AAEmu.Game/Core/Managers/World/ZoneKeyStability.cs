using System.Collections.Concurrent;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Sticky zone-key resolution for sea hulls. <see cref="WorldManager.GetZoneId"/> samples a 64 m
/// region grid, so a ship sailing along a zone seam flips keys every tick. That used to fire
/// WithdrawBoatFromZone + Create on every flip (see Slave.OnZoneChange since zone-authority boats).
/// </summary>
/// <remarks>
/// Hysteresis must bound handoff latency, not merely suppress flapping. The first revision required
/// strictly consecutive agreeing samples and dropped its pending state whenever the sampled key
/// differed from the pending candidate; a hull straddling a seam therefore never accumulated a run,
/// kept its previous zone as simulation authority long after the client had shown the hull inside
/// the next zone, and felt a sudden position/velocity snap when the handoff finally ran.
///
/// Now every non-zero sample counts as evidence for its key. A short run of identical samples still
/// commits immediately (the common crossing case), and once <see cref="MaxPendingSamples"/> samples
/// accrue without such a run, the majority candidate commits — ties go to the most recently sampled
/// key. A grazing hull whose samples mostly remain inside the current zone settles back where it
/// was, while a real crossing always hands off within <see cref="MaxPendingSamples"/> region
/// samples regardless of how evenly the seam straddle alternates.
/// </remarks>
public static class BoatZoneKeyStability
{
    /// <summary>Consecutive samples that must agree before the committed zone key changes.</summary>
    public const int RequiredConsecutiveSamples = 3;

    /// <summary>
    /// Pending samples allowed before the majority candidate commits regardless of run length.
    /// Four times the consecutive requirement keeps ordinary crossings on the fast path while
    /// capping seam-straddling handoff latency at roughly this many region samples (~one second
    /// at the rate <see cref="WorldManager.GetZoneId"/> is ticked for moving hulls).
    /// </summary>
    public const int MaxPendingSamples = RequiredConsecutiveSamples * 4;

    private sealed class Tracker
    {
        public readonly Dictionary<uint, uint> CandidateCounts = new();
        public readonly Dictionary<uint, uint> CandidateLastSeen = new();
        public uint ConsecutiveKey;
        public uint ConsecutiveCount;
        public uint Total;
        public uint NextSeenIndex;
    }

    private static readonly ConcurrentDictionary<uint, Tracker> Trackers = new();

    /// <summary>
    /// Returns <paramref name="currentKey"/> until a candidate zone key becomes stable.
    /// </summary>
    public static uint Resolve(uint boatObjId, uint sampledKey, uint currentKey)
    {
        // No identity, or no usable sample: hold the current key without disturbing pending
        // evidence, so gaps in sampling cannot stretch the latency bound.
        if (boatObjId == 0 || sampledKey == 0)
            return currentKey;

        var tracker = Trackers.GetOrAdd(boatObjId, _ => new Tracker());
        lock (tracker)
        {
            tracker.Total++;
            var seen = ++tracker.NextSeenIndex;
            tracker.CandidateCounts[sampledKey] = tracker.CandidateCounts.GetValueOrDefault(sampledKey) + 1;
            tracker.CandidateLastSeen[sampledKey] = seen;

            if (tracker.ConsecutiveKey == sampledKey)
                tracker.ConsecutiveCount++;
            else
            {
                tracker.ConsecutiveKey = sampledKey;
                tracker.ConsecutiveCount = 1;
            }

            // Fast path: a clean run of agreeing samples settles the question immediately — either
            // handing the hull to the new zone or confirming it never left the current one.
            if (tracker.ConsecutiveCount >= RequiredConsecutiveSamples)
            {
                Trackers.TryRemove(boatObjId, out _);
                return sampledKey;
            }

            // Latency bound: still straddling the seam after MaxPendingSamples — commit the
            // majority candidate so the previous zone cannot keep simulation authority
            // indefinitely. Ties go to the key seen most recently, matching where the hull is now.
            if (tracker.Total >= MaxPendingSamples)
            {
                var winner = sampledKey;
                var bestCount = 0u;
                var bestSeen = 0u;
                foreach (var (candidate, count) in tracker.CandidateCounts)
                {
                    var lastSeen = tracker.CandidateLastSeen[candidate];
                    if (count > bestCount || (count == bestCount && lastSeen > bestSeen))
                    {
                        winner = candidate;
                        bestCount = count;
                        bestSeen = lastSeen;
                    }
                }

                Trackers.TryRemove(boatObjId, out _);
                return winner;
            }

            return currentKey;
        }
    }

    /// <summary>Teleport / escape — accept the sampled key immediately.</summary>
    public static uint ForceCommit(uint boatObjId, uint sampledKey)
    {
        if (boatObjId != 0)
            Trackers.TryRemove(boatObjId, out _);

        return sampledKey;
    }

    public static void Clear(uint boatObjId)
    {
        if (boatObjId != 0)
            Trackers.TryRemove(boatObjId, out _);
    }
}

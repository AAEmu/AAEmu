using System.Collections.Concurrent;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Sticky zone-key resolution for sea hulls. <see cref="WorldManager.GetZoneId"/> samples a 64 m
/// region grid, so a ship sailing along a zone seam flips keys every tick. That used to fire
/// WithdrawBoatFromZone + Create on every flip (see Slave.OnZoneChange since zone-authority boats).
/// </summary>
public static class BoatZoneKeyStability
{
    /// <summary>Consecutive samples that must agree before the committed zone key changes.</summary>
    public const int RequiredConsecutiveSamples = 3;

    private sealed class Tracker
    {
        public uint PendingZoneKey;
        public int PendingCount;
    }

    private static readonly ConcurrentDictionary<uint, Tracker> Trackers = new();

    /// <summary>
    /// Returns <paramref name="currentKey"/> until <paramref name="sampledKey"/> is stable.
    /// </summary>
    public static uint Resolve(uint boatObjId, uint sampledKey, uint currentKey)
    {
        if (boatObjId == 0)
            return currentKey;

        if (sampledKey == 0 || sampledKey == currentKey)
        {
            Trackers.TryRemove(boatObjId, out _);
            return sampledKey == 0 ? currentKey : sampledKey;
        }

        var tracker = Trackers.GetOrAdd(boatObjId, _ => new Tracker());
        lock (tracker)
        {
            if (tracker.PendingZoneKey != sampledKey)
            {
                tracker.PendingZoneKey = sampledKey;
                tracker.PendingCount = 1;
            }
            else
            {
                tracker.PendingCount++;
            }

            if (tracker.PendingCount < RequiredConsecutiveSamples)
                return currentKey;

            Trackers.TryRemove(boatObjId, out _);
            return sampledKey;
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

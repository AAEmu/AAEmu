namespace AAEmu.Game.Models.Game.Faction;

public enum FactionPowerStanding : byte
{
    Automatic = 0,
    Superior = 1,
    Inferior = 2,
    Normal = 3
}

/// <summary>
/// Faction activity rules as the client's faction activity tooltip describes them: the week resets on Sunday
/// 00:00, the activity score is the sum of a faction's point counters, the highest scorer is superior and the
/// lowest inferior, a runner-up close to either end shares that standing, and a quiet server has neither.
/// "Close" is power_relation_factor (110 = within 10%), "quiet" is a top score under power_qualifier.
/// </summary>
public static class FactionPowerRules
{
    private static readonly DateTime FirstSunday = new(1970, 1, 4, 0, 0, 0, DateTimeKind.Utc);

    public static uint WeekNumber(DateTime utc) => (uint)Math.Max(0, Math.Floor((utc - FirstSunday).TotalDays / 7));

    public static DateTime WeekStart(uint weekNum) => FirstSunday.AddDays(7.0 * weekNum);

    /// <summary>The activity score exactly as the client adds it up for the activity stage.</summary>
    public static int Total(FactionPowerScore score) =>
        score == null
            ? 0
            : (int)(score.QuestPoint + 0.5f) + (int)score.SiegePoint + (int)score.SiegeMaintainPoint +
              (int)score.CompetitionPoint + (int)score.RaidPoint + (int)score.RaidMaintainPoint +
              (int)score.RaidJointPoint + (int)score.ImmigratePoint;

    public static (HashSet<uint> Superiors, HashSet<uint> Inferiors) Classify(
        IReadOnlyDictionary<uint, int> totals, int qualifier, int relationFactorPercent)
    {
        var superiors = new HashSet<uint>();
        var inferiors = new HashSet<uint>();
        var ranked = totals.OrderByDescending(t => t.Value).ThenBy(t => t.Key).ToList();
        if (ranked.Count < 2 || ranked[0].Value < qualifier)
            return (superiors, inferiors);

        var factor = Math.Max(100, relationFactorPercent) / 100.0;
        bool Close(int higher, int lower) => higher <= lower * factor;

        superiors.Add(ranked[0].Key);
        if (ranked.Count > 2 && Close(ranked[0].Value, ranked[1].Value))
            superiors.Add(ranked[1].Key);

        var last = ranked[^1];
        if (!superiors.Contains(last.Key))
            inferiors.Add(last.Key);
        if (ranked.Count > 2 && !superiors.Contains(ranked[^2].Key) && Close(ranked[^2].Value, last.Value))
            inferiors.Add(ranked[^2].Key);

        return (superiors, inferiors);
    }

    /// <summary>GM-forced standings replace the computed ones for their factions.</summary>
    public static void ApplyOverrides(HashSet<uint> superiors, HashSet<uint> inferiors,
        IReadOnlyDictionary<uint, FactionPowerStanding> overrides)
    {
        foreach (var (factionId, standing) in overrides)
        {
            superiors.Remove(factionId);
            inferiors.Remove(factionId);
            if (standing == FactionPowerStanding.Superior)
                superiors.Add(factionId);
            else if (standing == FactionPowerStanding.Inferior)
                inferiors.Add(factionId);
        }
    }

    /// <summary>Share of last week's points a faction keeps into the new week (faction_power_*_maintain percent).</summary>
    public static uint Maintain(uint points, uint carried, int percent) =>
        (uint)Math.Round(((double)points + carried) * Math.Max(0, percent) / 100.0);

    /// <summary>A faction's first row of a week: only what it carries over from last week.</summary>
    public static FactionPowerScore NewWeekRow(uint week, uint factionId, FactionPowerScore lastWeek,
        int siegeMaintainPercent, int raidMaintainPercent)
    {
        var score = new FactionPowerScore { WeekNum = week, FactionId = factionId };
        if (lastWeek != null)
        {
            score.SiegeMaintainPoint = Maintain(lastWeek.SiegePoint, lastWeek.SiegeMaintainPoint, siegeMaintainPercent);
            score.RaidMaintainPoint = Maintain(lastWeek.RaidPoint, lastWeek.RaidMaintainPoint, raidMaintainPercent);
        }

        return score;
    }

    /// <summary>
    /// The week a siege win counts for: the week its fight ended, which is not the week it is settled in when
    /// the server was down at the end. Without a known end, or one still ahead, the current week.
    /// </summary>
    public static uint SiegeAwardWeek(DateTime? siegeEndUtc, DateTime nowUtc) =>
        WeekNumber(siegeEndUtc is { } end && end <= nowUtc ? end.AddSeconds(-1) : nowUtc);

    /// <summary>
    /// The rows a siege award writes, whole, together with its award marker. A win of the current week adds to
    /// this week's row. A win of last week adds to last week's row and changes what this week carries over.
    /// An older win has no rows in memory (both null, the points are only added in the database). A win of a
    /// week that has not been rolled over to yet is deferred, or the rollover would save over it.
    /// </summary>
    public static (bool Defer, FactionPowerScore Won, FactionPowerScore Carried) PlanSiegeAward(uint awardWeek,
        uint points, uint currentWeek, FactionPowerScore lastWeek, FactionPowerScore thisWeek, int siegeMaintainPercent)
    {
        if (awardWeek > currentWeek)
            return (true, null, null);

        if (awardWeek == currentWeek)
        {
            var won = thisWeek.Clone();
            won.SiegePoint += points;
            return (false, won, null);
        }

        if (awardWeek + 1 == currentWeek)
        {
            var won = lastWeek?.Clone() ?? new FactionPowerScore { WeekNum = awardWeek, FactionId = thisWeek.FactionId };
            won.SiegePoint += points;
            var carried = thisWeek.Clone();
            carried.SiegeMaintainPoint = Maintain(won.SiegePoint, won.SiegeMaintainPoint, siegeMaintainPercent);
            return (false, won, carried);
        }

        return (false, null, null);
    }
}

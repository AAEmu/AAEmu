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
}

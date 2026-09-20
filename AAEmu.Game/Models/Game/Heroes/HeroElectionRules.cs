using System.Globalization;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Heroes;

/// <summary>
/// Pure decisions for the monthly Hero election: which seats are elected, how event-state entries are
/// shaped for the client, and the calendar rules for the Hero's weekly/daily allowances. Data
/// (thresholds, seat counts, phase windows) comes from <c>hero_*</c>; nothing here hardcodes a faction.
/// </summary>
public static class HeroElectionRules
{
    /// <summary>Event-state byte that announces a phase beginning (banner + HUD icon).</summary>
    public const byte StateEntering = 0;

    /// <summary>Event-state byte for a silent resync of a running phase.</summary>
    public const byte StateRunning = 1;

    /// <summary>Event-state byte that announces a phase ending.</summary>
    public const byte StateLeaving = 2;

    /// <summary>
    /// A ranked candidate holds a seat when a <c>hero_rewards</c> row exists for that ranking. Rank 1 is the
    /// faction's Hero; the other seats are lower-grade heroes.
    /// </summary>
    public static bool HoldsSeat(int ranking, int seats) => ranking >= 1 && ranking <= seats;

    /// <summary>
    /// Builds the per-phase entries for one event-state send. One entry per distinct running phase (the
    /// client keys its slots by phase, not faction). A real transition marks the entered phase
    /// <see cref="StateEntering"/> and, when the phase just left is no longer running anywhere, adds it as
    /// <see cref="StateLeaving"/>. A plain resync (<paramref name="leaving"/> null) sends everything as
    /// <see cref="StateRunning"/> so banners do not re-fire.
    /// </summary>
    public static List<HeroEventStateEntry> BuildEventStateEntries(
        IReadOnlyDictionary<uint, (HeroPhase Phase, uint SeasonId)> phaseByFaction,
        (uint Season, HeroPhase Phase)? leaving)
    {
        var distinct = new Dictionary<HeroPhase, uint>();
        foreach (var (phase, seasonId) in phaseByFaction.Values)
        {
            if (phase != HeroPhase.None)
                distinct.TryAdd(phase, seasonId);
        }

        var entries = new List<HeroEventStateEntry>();
        var enteringState = leaving.HasValue ? StateEntering : StateRunning;
        foreach (var (phase, seasonId) in distinct)
            entries.Add(new HeroEventStateEntry(phase, seasonId, enteringState));

        if (leaving is { Phase: not HeroPhase.None } l && !distinct.ContainsKey(l.Phase))
            entries.Add(new HeroEventStateEntry(l.Phase, l.Season, StateLeaving));

        return entries;
    }

    public static bool IsSameUtcDay(DateTime a, DateTime b) =>
        ServerCalendar.AsUtc(a).Date == ServerCalendar.AsUtc(b).Date;

    /// <summary>True when both instants fall in the same UTC clock hour (year/month/day/hour).</summary>
    public static bool IsSameUtcHour(DateTime a, DateTime b)
    {
        var ua = ServerCalendar.AsUtc(a);
        var ub = ServerCalendar.AsUtc(b);
        return ua.Year == ub.Year && ua.Month == ub.Month && ua.Day == ub.Day && ua.Hour == ub.Hour;
    }

    /// <summary>
    /// A member may accept one order per UTC clock hour (same year/month/day/hour).
    /// Compact has no hourly duration knob — the shipped mobilization
    /// <c>content_configs</c> are daily-max, accept-window, level, and leadership.
    /// An elapsed-hour window would invent a 3600 s row. Epoch means never accepted.
    /// </summary>
    public static bool CanAcceptMobilizationAgainThisHour(DateTime lastAcceptUtc, DateTime nowUtc)
    {
        var last = ServerCalendar.AsUtc(lastAcceptUtc);
        if (last <= DateTime.UnixEpoch)
            return true;
        return !IsSameUtcHour(last, nowUtc);
    }

    /// <summary>
    /// The accept-popup checkbox "do not receive today". A default / epoch stamp means they have not muted.
    /// The mute lasts the rest of the UTC day and clears at the next UTC midnight.
    /// </summary>
    public static bool IsMobilizationOrderMutedToday(DateTime lastNotRecvUtc, DateTime nowUtc)
    {
        var last = ServerCalendar.AsUtc(lastNotRecvUtc);
        if (last <= DateTime.UnixEpoch)
            return false;
        return IsSameUtcDay(last, nowUtc);
    }

    /// <summary>Checking the box stamps now; clearing it returns the clock to epoch (unmuted).</summary>
    public static DateTime MobilizationOrderNotRecvStamp(bool mute, DateTime nowUtc) =>
        mute ? ServerCalendar.AsUtc(nowUtc) : DateTime.UnixEpoch;

    /// <summary>
    /// Whether a nation member is offered the accept popup for a new issue. Daily mute and the
    /// one-accept-per-UTC-hour gate both suppress it; the hour gate lifts at the next clock hour.
    /// </summary>
    public static bool ShouldOfferMobilizationOrder(
        DateTime lastNotRecvUtc, DateTime lastAcceptUtc, DateTime nowUtc) =>
        !IsMobilizationOrderMutedToday(lastNotRecvUtc, nowUtc)
        && CanAcceptMobilizationAgainThisHour(lastAcceptUtc, nowUtc);

    /// <summary>Authored stand next to a rally flag (a loaded <c>return_point.g</c> pad).</summary>
    public readonly record struct RallyStand(float X, float Y, float Z, float YawRad, uint ZoneId);

    /// <summary>
    /// Pads authored in the flag's zone. An empty list keeps the issuer-position
    /// fallback — do not hand every pad to <see cref="TryPickRallyStand"/> (no distance cap).
    /// </summary>
    public static IReadOnlyList<RallyStand> PadsForFlagZone(uint flagZoneId, IReadOnlyList<RallyStand> pads)
    {
        if (pads == null || pads.Count == 0 || flagZoneId == 0)
            return [];

        List<RallyStand> same = null;
        foreach (var pad in pads)
        {
            if (pad.ZoneId != flagZoneId)
                continue;
            same ??= [];
            same.Add(pad);
        }

        return same ?? [];
    }

    /// <summary>Picks the stand closest to the flag on the XY plane. Empty list → no stand.</summary>
    public static bool TryPickRallyStand(float flagX, float flagY, IReadOnlyList<RallyStand> pads, out RallyStand stand)
    {
        stand = default;
        if (pads == null || pads.Count == 0)
            return false;

        var best = float.PositiveInfinity;
        var found = false;
        foreach (var pad in pads)
        {
            var dx = pad.X - flagX;
            var dy = pad.Y - flagY;
            var d = dx * dx + dy * dy;
            if (d >= best)
                continue;
            best = d;
            stand = pad;
            found = true;
        }

        return found;
    }

    public static bool IsSameIsoWeek(DateTime a, DateTime b)
    {
        var ua = ServerCalendar.AsUtc(a);
        var ub = ServerCalendar.AsUtc(b);
        return ISOWeek.GetYear(ua) == ISOWeek.GetYear(ub) && ISOWeek.GetWeekOfYear(ua) == ISOWeek.GetWeekOfYear(ub);
    }

    /// <summary>Seconds until the next UTC midnight; used as the dialog's "next give" countdown.</summary>
    public static uint SecondsUntilNextUtcDay(DateTime utcNow)
    {
        var now = ServerCalendar.AsUtc(utcNow);
        return (uint)Math.Max(0, (now.Date.AddDays(1) - now).TotalSeconds);
    }

    /// <summary>
    /// Whether a Hero may issue another Mobilization Order today
    /// (<c>content_configs.mobilization_order_daily_count_max</c>). A zero cap closes the feature.
    /// </summary>
    public static bool CanIssueMobilizationOrder(int issuedToday, int dailyMax) =>
        dailyMax > 0 && issuedToday < dailyMax;

    /// <summary>
    /// Whether a member may answer an order: the accept window
    /// (<c>mobilization_order_accept_delay</c>) is still open and the member meets
    /// <c>mobilization_order_level</c> / <c>mobilization_order_leadership_point</c>.
    /// </summary>
    public static bool CanAcceptMobilizationOrder(DateTime utcNow, DateTime expiresAtUtc, int level, int leadership, int minLevel, int minLeadership) =>
        ServerCalendar.AsUtc(utcNow) < ServerCalendar.AsUtc(expiresAtUtc) && level >= minLevel && leadership >= minLeadership;

    /// <summary>
    /// A new cycle snapshots the actual current total, including zero, then current restarts.
    /// Retry protection is the cycle marker, not "keep the old period when current is 0".
    /// </summary>
    public static (int Period, int Current) RollLeadershipPeriod(int period, int current)
    {
        _ = period;
        return (current, 0);
    }

    public static bool CanTransferToMobilizationFlag(bool flagFound, bool hasParentWorld) =>
        flagFound && hasParentWorld;

    public static bool NeedsInstanceLoad(uint fromInstanceId, uint toInstanceId) =>
        fromInstanceId != toInstanceId;

    /// <summary>
    /// The Hero activity bonus pays when the term leadership and issued-order counts reach the tier's
    /// thresholds and every Hero-board step of the tier has been completed its required number of times.
    /// </summary>
    public static bool MeetsBonusConditions(
        int termLeadership,
        int ordersIssuedThisTerm,
        HeroBonus bonus,
        IReadOnlyList<HeroBonusTodayAssignment> assignments,
        IReadOnlyDictionary<uint, int> stepProgress)
    {
        if (bonus == null)
            return false;
        if (termLeadership < bonus.LeadershipPoint || ordersIssuedThisTerm < bonus.MobilizationOrderCount)
            return false;

        foreach (var assignment in assignments)
        {
            if (!stepProgress.TryGetValue(assignment.TodayQuestStepId, out var done) || done < assignment.Count)
                return false;
        }

        return true;
    }

    public readonly record struct DominionPointCounters(uint Daily, uint Weekly, uint RemainSeconds);

    /// <summary>
    /// Today's and this week's give counts from the give log (newest first), and the seconds until the next
    /// give is allowed: the cooldown remainder, or the time to the next UTC day when today's limit is used.
    /// </summary>
    public static DominionPointCounters DominionPointState(IReadOnlyList<DateTime> weekGivesNewestFirst, DateTime utcNow, uint dailyMax, TimeSpan cooldown)
    {
        var now = ServerCalendar.AsUtc(utcNow);
        uint daily = 0;
        foreach (var give in weekGivesNewestFirst)
        {
            if (IsSameUtcDay(give, now))
                daily++;
        }

        var weekly = (uint)weekGivesNewestFirst.Count;
        uint remain = 0;
        if (dailyMax > 0 && daily >= dailyMax)
        {
            remain = SecondsUntilNextUtcDay(now);
        }
        else if (weekGivesNewestFirst.Count > 0)
        {
            var since = now - ServerCalendar.AsUtc(weekGivesNewestFirst[0]);
            if (since < cooldown)
                remain = (uint)Math.Ceiling((cooldown - since).TotalSeconds);
        }

        return new DominionPointCounters(daily, weekly, remain);
    }

    /// <summary>
    /// How many candidate ids a voting packet may allocate: the signed count, clipped to the bytes still
    /// on the wire (each pick is a u64, plus a trailing voter u64). A huge client count cannot grow the
    /// list past what the packet actually contains.
    /// </summary>
    public static int BallotPickCount(int requested, int remainingBytes)
    {
        if (requested <= 0 || remainingBytes < sizeof(ulong))
            return 0;
        var maxByWire = (remainingBytes - sizeof(ulong)) / sizeof(ulong);
        return requested < maxByWire ? requested : maxByWire;
    }

    /// <summary>
    /// Whether a ballot is acceptable: non-empty, no more picks than the faction has seats, and cast by a
    /// character meeting the <c>hero_conditions</c> voter thresholds against the previous period's
    /// leadership (the frozen figure the client also gates on).
    /// </summary>
    public static bool CanCastBallot(int picks, int seats, int voterLevel, int periodLeadership, int votableLevel, int votableLeadership)
    {
        if (picks <= 0 || picks > Math.Max(seats, 1))
            return false;
        return voterLevel >= votableLevel && periodLeadership >= votableLeadership;
    }

    /// <summary>
    /// Candidacy gates from <c>hero_conditions.hero_candidate_min_level</c> /
    /// <c>hero_candidate_min_point</c> against current-period leadership.
    /// </summary>
    public static bool MeetsCandidateThreshold(int level, int leadershipPoint, int minLevel, int minPoint) =>
        level >= minLevel && leadershipPoint >= minPoint;

    /// <summary>
    /// Rows whose mail has not been claimed. The live path claims (sets sent=1) before Send so a
    /// crash after delivery cannot enqueue a second item-bearing reward.
    /// </summary>
    public static List<uint> PendingMailCharacterIds(IEnumerable<(uint CharacterId, bool MailSent)> rows)
    {
        var pending = new List<uint>();
        foreach (var (characterId, mailSent) in rows)
        {
            if (!mailSent)
                pending.Add(characterId);
        }

        return pending;
    }

    /// <summary>
    /// Freeze roster: persisted rows plus anyone online whose live current-period
    /// leadership already meets the gate. Live wins when both exist — a GM set or
    /// an unsaved award is not in MySQL yet.
    /// </summary>
    public static List<(uint CharacterId, int Points)> MergeAndRankCandidates(
        IEnumerable<(uint CharacterId, int Points)> persistedQualified,
        IEnumerable<(uint CharacterId, int Level, int Points)> live,
        int minLevel,
        int minPoint,
        int scope)
    {
        var byId = new Dictionary<uint, int>();
        foreach (var (characterId, points) in persistedQualified)
            byId[characterId] = points;

        foreach (var (characterId, level, points) in live)
        {
            if (!MeetsCandidateThreshold(level, points, minLevel, minPoint))
                continue;
            byId[characterId] = points;
        }

        var take = Math.Max(scope, 1);
        return byId
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(take)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }
}

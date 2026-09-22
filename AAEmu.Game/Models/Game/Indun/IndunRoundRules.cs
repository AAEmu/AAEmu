namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// Pure rules for the per-copy round counter that <c>indun_rounds</c> drives through
/// <c>IndunActionNextRound</c> and <c>IndunActionRoundAlarm</c>, and that the client shows through
/// SCIndunInitialRoundInfoPacket (0x2D9, serializer x2game-dev.dll 0x39c543c0: curRound i8, totalRound i8,
/// playing) and SCIndunRoundPlayStatusPacket (0x2DB, serializer 0x39c544e0: playing, success, round i8,
/// nextRoundBoss, showUi). Neither binary constructs the packets, so what each flag means comes from the
/// content chains cited on the rule that uses it.
/// </summary>
public static class IndunRoundRules
{
    /// <summary><c>enum_indun_round_alarm_kinds</c>: 1 start, 2 end.</summary>
    public const byte AlarmKindStart = 1;
    public const byte AlarmKindEnd = 2;

    /// <summary><c>enum_indun_doodad_check_statuses</c>: 1 always, 2 timer, 3 no_timer.</summary>
    public const uint CheckStatusAlways = 1;
    public const uint CheckStatusTimer = 2;
    public const uint CheckStatusNoTimer = 3;

    /// <summary>Only copies of a zone group with <c>indun_rounds</c> rows get round packets; the rest stay as before.</summary>
    public static bool HasRounds(int totalRounds) => totalRounds > 0;

    /// <summary>
    /// <c>indun_action_next_rounds.round_add</c> is 1, 4 or 5 on seven rows and 0 on the eighth. The counter
    /// never passes the last round; the clamp is the guard for a row that would overshoot.
    /// </summary>
    public static int NextRound(int current, int total, int roundAdd)
    {
        if (!HasRounds(total))
            return current;

        return Math.Clamp(current + roundAdd, 0, total);
    }

    /// <summary>
    /// The copy is complete when a NextRound fires while the counter already sits on the last round, or on
    /// the explicit round_add 0 (action 327 "last round clear", the only such row). Never before round 1.
    /// </summary>
    public static bool IsCompletion(int current, int total, int roundAdd) =>
        HasRounds(total) && current > 0 && (current >= total || roundAdd == 0);

    public static bool ShouldGrantCompletion(bool alreadyCompleted) => !alreadyCompleted;

    public static bool IsTimerRunning(DateTime? roundStartedUtc, int timerSeconds, DateTime nowUtc) =>
        roundStartedUtc.HasValue && timerSeconds > 0 && nowUtc < roundStartedUtc.Value.AddSeconds(timerSeconds);

    /// <summary>
    /// <c>check_status_id</c> gates <c>IndunEventDoodadPhaseChanged</c> on the round timer. Zone group 125 has
    /// two events on the same doodad phase 13555/39273: "1RO clear(+4)" with status 2 (action 301) and
    /// "1RO Clear(+1)" with status 3 (action 303), so a round cleared inside its 120 s timer skips ahead and
    /// one cleared after it advances by one. An unknown status id fires nothing.
    /// </summary>
    public static bool PhaseCheckMatches(uint checkStatusId, bool timerRunning) => checkStatusId switch
    {
        CheckStatusAlways => true,
        CheckStatusTimer => timerRunning,
        CheckStatusNoTimer => !timerRunning,
        _ => false,
    };

    /// <summary>
    /// <c>boss_round</c> of the round whose number is <paramref name="round"/>, the round the play status packet
    /// reports and, at an end alarm, the one about to be played; false without such a row.
    /// </summary>
    public static bool IsBossRound(IReadOnlyList<IndunRound> rounds, int round)
    {
        if (rounds == null)
            return false;
        foreach (var row in rounds)
        {
            if (row.Round == round)
                return row.BossRound;
        }

        return false;
    }

    /// <summary>The wire fields curRound, totalRound and round are one signed byte each.</summary>
    public static sbyte ToWireRound(int round) => (sbyte)Math.Clamp(round, 0, sbyte.MaxValue);

    /// <summary>
    /// The end alarm reports success when a NextRound ran since the round started. Every content chain that
    /// ends a cleared round passes a NextRound first (125: 303 to 307 to 309; 126: 313 to 326 and 327 to 328)
    /// and the two failure chains do not (125: 306 to 309 "All dead"; 126: 317 "relic destroyed").
    /// </summary>
    public static bool EndAlarmSuccess(bool nextRoundRanSinceStart) => nextRoundRanSinceStart;

    /// <summary>
    /// The round-start spawn actions 304 and 311 carry <c>npc_spawner_spawn_effects.spawner_id</c> 0 while
    /// their zone groups' <c>indun_rounds</c> rows carry the spawner, so 0 means the current round's spawner.
    /// The other 19 indun spawn actions carry their own id.
    /// </summary>
    public static uint ResolveSpawnerId(uint effectSpawnerId, uint currentRoundSpawnerId) =>
        effectSpawnerId != 0 ? effectSpawnerId : currentRoundSpawnerId;
}

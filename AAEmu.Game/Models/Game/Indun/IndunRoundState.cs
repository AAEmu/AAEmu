namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// Round counter of one dungeon copy. Holds no world state; the <see cref="Dungeon"/> that owns it sends
/// the packets and runs the reward hook. Every decision goes through <see cref="IndunRoundRules"/>.
/// </summary>
public sealed class IndunRoundState
{
    private readonly IReadOnlyList<IndunRound> _rounds;
    private readonly HashSet<uint> _mailRewardKinds = [];
    private DateTime? _roundStartedUtc;

    public IndunRoundState(IReadOnlyList<IndunRound> rounds)
    {
        _rounds = rounds ?? [];
        var total = 0;
        foreach (var round in _rounds)
            total = Math.Max(total, round.Round);
        TotalRounds = total;
    }

    /// <summary>Highest <c>indun_rounds.round</c> of the zone group; 0 when it has no rows.</summary>
    public int TotalRounds { get; }

    /// <summary>0 until the first NextRound; the wire <c>curRound</c> / <c>round</c>.</summary>
    public int CurrentRound { get; private set; }

    /// <summary>True between a start alarm and the next end alarm; the wire <c>playing</c>.</summary>
    public bool Playing { get; private set; }

    /// <summary>Set once per copy, on the NextRound that completes it.</summary>
    public bool Completed { get; private set; }

    /// <summary>A NextRound ran since the last start alarm; the end alarm's <c>success</c>.</summary>
    public bool NextRoundRanSinceStart { get; private set; }

    public bool HasRounds => IndunRoundRules.HasRounds(TotalRounds);

    public IndunRound Current
    {
        get
        {
            foreach (var round in _rounds)
            {
                if (round.Round == CurrentRound)
                    return round;
            }

            return null;
        }
    }

    public uint CurrentSpawnerId => Current?.SpawnerId ?? 0;

    public bool NextRoundIsBoss => IndunRoundRules.NextRoundIsBoss(_rounds, CurrentRound);

    public bool IsTimerRunning(DateTime nowUtc) =>
        IndunRoundRules.IsTimerRunning(_roundStartedUtc, Current?.TimerSeconds ?? 0, nowUtc);

    /// <summary>
    /// Applies one <c>IndunActionNextRound</c>. Returns true only on the call that completes the copy; a
    /// second completion signal (a doubled event, a relog replaying the chain) returns false.
    /// </summary>
    public bool ApplyNextRound(int roundAdd)
    {
        NextRoundRanSinceStart = true;
        var completes = IndunRoundRules.IsCompletion(CurrentRound, TotalRounds, roundAdd);
        CurrentRound = IndunRoundRules.NextRound(CurrentRound, TotalRounds, roundAdd);
        if (!completes || !IndunRoundRules.ShouldGrantCompletion(Completed))
            return false;

        Completed = true;
        return true;
    }

    /// <summary>Start alarm: the current round is in play and its timer, if any, starts now.</summary>
    public void StartRound(DateTime nowUtc)
    {
        Playing = true;
        _roundStartedUtc = nowUtc;
        NextRoundRanSinceStart = false;
    }

    /// <summary>End alarm. Returns the <c>success</c> flag for the play status packet.</summary>
    public bool EndRound()
    {
        Playing = false;
        _roundStartedUtc = null;
        var success = IndunRoundRules.EndAlarmSuccess(NextRoundRanSinceStart);
        NextRoundRanSinceStart = false;
        return success;
    }

    /// <summary>One mail reward per copy per <c>instance_reward_kind_id</c>; false on a repeat.</summary>
    public bool TryMarkMailReward(uint instanceRewardKindId) => _mailRewardKinds.Add(instanceRewardKindId);
}

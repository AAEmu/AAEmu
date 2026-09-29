namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// The stored score of one <c>zone_score_kinds</c> row for one zone group, plus the catalog level
/// that score resolves to. A kind that has never scored carries score zero and the shipped
/// level-zero baseline, which is what a fresh row restores to.
/// </summary>
public readonly record struct ZoneScoreRuntimeEntry(uint KindId, uint ZoneGroupId, long Score, int Level);

/// <summary>
/// One applied zone-score change: the entry before and after, the delta that was requested, the
/// delta that was actually credited, and the catalog level transition the change produced.
/// </summary>
/// <param name="RequestedDelta">The signed delta the caller asked for.</param>
/// <param name="AppliedDelta">
/// <paramref name="RequestedDelta"/> reduced by <c>max_score</c> clamping. A caller can therefore
/// tell "did not score at all" (applied zero) from "scored and hit the cap" (applied below the
/// request) — the two are different outcomes and the senders need to tell them apart too.
/// </param>
public readonly record struct ZoneScoreApplication(
    uint KindId,
    uint ZoneGroupId,
    long PreviousScore,
    long Score,
    int RequestedDelta,
    int AppliedDelta,
    int PreviousLevel,
    int Level,
    bool LevelChanged,
    bool Clamped);

/// <summary>
/// Why a zone-score entry was cleared. Each cause maps to a <c>zone_score_kinds</c> reset column, so
/// a kind that does not opt into a cause keeps its score across that event.
/// </summary>
public enum ZoneScoreResetCause
{
    /// <summary>A character entered the kind's zone group (<c>reset_zone_in</c>).</summary>
    ZoneIn,

    /// <summary>A character left the kind's zone group (<c>reset_zone_out</c>).</summary>
    ZoneOut,

    /// <summary>The content buff for the kind or its level went away (<c>reset_buff_destroyed</c>).</summary>
    BuffDestroyed,

    /// <summary>The content quest for the kind was removed or abandoned (<c>reset_quest_removed</c>).</summary>
    QuestRemoved
}

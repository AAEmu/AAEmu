using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// Pure zone-score rules backed only by the shipped <c>zone_score_kinds</c> and
/// <c>zone_score_levels</c> rows. The resolver selects the highest catalog level whose
/// <c>req_score</c> is not above the score, and the applier clamps the result to the kind's
/// <c>max_score</c>. It owns no score table, sends no packet, and persists nothing.
/// </summary>
public static class ZoneScoreRules
{
    /// <summary>
    /// Resolves the catalog level for a score, including the shipped level-zero baseline. A score
    /// below the level-zero requirement is a content error: every shipped level-zero row requires
    /// zero, so a negative floor would mean the kind cannot represent its own start.
    /// </summary>
    public static ZoneScoreLevel ResolveLevel(FactionScoringGameData gameData, uint kindId, long score)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var levels = gameData.GetZoneScoreLevels(kindId);
        if (levels.Count == 0)
            throw new InvalidOperationException($"Zone score kind {kindId} has no level rows.");

        var baseLevel = levels.SingleOrDefault(level => level.Level == 0);
        if (baseLevel is null)
            throw new InvalidOperationException($"Zone score kind {kindId} has no level-zero baseline.");
        if (baseLevel.RequiredScore > 0)
            throw new InvalidOperationException(
                $"Zone score kind {kindId} has a level-zero baseline requiring {baseLevel.RequiredScore}.");

        var selected = levels
            .Where(level => level.RequiredScore <= score)
            .OrderBy(level => level.Level)
            .LastOrDefault();
        if (selected is null)
            throw new InvalidOperationException($"Zone score kind {kindId} has no level for score {score}.");

        return selected;
    }

    /// <summary>
    /// Applies a signed delta to a score under the kind's <c>max_score</c> cap and reports the
    /// resulting level. The cap is a ceiling only: a negative score is not clamped up, because no
    /// shipped column states a floor.
    /// </summary>
    public static ZoneScoreApplication ApplyDelta(
        FactionScoringGameData gameData,
        uint zoneGroupId,
        uint kindId,
        long currentScore,
        int scoreDelta)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var kind = gameData.GetZoneScoreKind(kindId);
        var content = gameData.GetZoneScoreContent(kind.ContentId);
        if (content.ZoneGroupId != zoneGroupId)
            throw new InvalidOperationException(
                $"Zone score kind {kindId} belongs to zone group {content.ZoneGroupId}, not {zoneGroupId}.");

        long uncapped;
        try
        {
            uncapped = checked(currentScore + scoreDelta);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                $"Zone score kind {kindId} score overflow while applying {scoreDelta}.",
                exception);
        }

        var next = Math.Min(uncapped, kind.MaxScore);
        var previousLevel = ResolveLevel(gameData, kindId, currentScore);
        var currentLevel = ResolveLevel(gameData, kindId, next);
        return new ZoneScoreApplication(
            kindId,
            zoneGroupId,
            currentScore,
            next,
            scoreDelta,
            checked((int)(next - currentScore)),
            previousLevel.Level,
            currentLevel.Level,
            previousLevel.Level != currentLevel.Level,
            next != uncapped);
    }

    /// <summary>
    /// True when the shipped <c>zone_score_kinds</c> row opts into this reset cause. A kind that
    /// does not opt in keeps its score across the event that would otherwise clear it.
    /// </summary>
    public static bool ResetsOn(FactionScoringGameData gameData, uint kindId, ZoneScoreResetCause cause)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var kind = gameData.GetZoneScoreKind(kindId);
        return cause switch
        {
            ZoneScoreResetCause.ZoneIn => kind.ResetZoneIn,
            ZoneScoreResetCause.ZoneOut => kind.ResetZoneOut,
            ZoneScoreResetCause.BuffDestroyed => kind.ResetBuffDestroyed,
            ZoneScoreResetCause.QuestRemoved => kind.ResetQuestRemoved,
            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown zone-score reset cause.")
        };
    }
}

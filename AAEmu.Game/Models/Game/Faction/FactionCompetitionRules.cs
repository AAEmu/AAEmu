using AAEmu.Game.GameData;

using NLog;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// The event kinds a faction-competition score can be earned from. A player kill is deliberately
/// absent: no shipped column says which characters count for that award, so selecting them needs an
/// explicit caller rather than a rule this class would have to invent.
/// </summary>
public enum FactionCompetitionEventKind
{
    /// <summary>An NPC listed in <c>faction_competition_npc_infos</c> was killed.</summary>
    NpcKill,

    /// <summary>A quest context listed in <c>faction_competition_quest_infos</c> was completed.</summary>
    QuestComplete
}

/// <summary>
/// Pure faction-competition rules backed only by the shipped <c>faction_competitions</c> row and
/// the faction scores a caller supplies. The NPC and quest link tables are eligibility catalogs:
/// they say which competitions an event counts for, while the point value and the winning
/// threshold always come from the competition row itself.
/// </summary>
public static class FactionCompetitionRules
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Decodes a <c>point_reset_id</c> into its behaviour. The shipped
    /// <c>enum_faction_competition_reset_state_kinds</c> names are matched exactly; an unknown name
    /// is refused so a new shipped kind cannot be silently treated as one of the three known
    /// behaviours.
    /// </summary>
    public static FactionCompetitionResetStateKind DecodeResetState(FactionScoringGameData gameData, uint pointResetId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var resetState = gameData.GetResetState(pointResetId);
        return resetState.Name switch
        {
            "all" => FactionCompetitionResetStateKind.All,
            "winnerOnly" => FactionCompetitionResetStateKind.WinnerOnly,
            "all_ignoreReqPoint" => FactionCompetitionResetStateKind.AllIgnoreRequiredPoints,
            _ => throw new InvalidOperationException(
                $"Faction competition reset state {pointResetId} has unsupported name '{resetState.Name}'.")
        };
    }

    /// <summary>
    /// The competitions a kill of <paramref name="npcTemplateId"/> counts for, from
    /// <c>faction_competition_npc_infos</c>. A shipped link that names the same NPC twice yields one
    /// contribution per competition, so a duplicated link row cannot double a faction's score.
    /// </summary>
    public static IReadOnlyList<uint> CompetitionsForNpcKill(
        FactionScoringGameData gameData,
        uint npcTemplateId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        return gameData.CompetitionNpcLinks
            .Where(link => link.NpcId == npcTemplateId)
            .Select(link => link.CompetitionId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    /// <summary>
    /// The competitions a completion of <paramref name="questContextId"/> counts for, from
    /// <c>faction_competition_quest_infos</c>, de-duplicated the same way as the NPC links.
    /// </summary>
    public static IReadOnlyList<uint> CompetitionsForQuestComplete(
        FactionScoringGameData gameData,
        uint questContextId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        return gameData.CompetitionQuestLinks
            .Where(link => link.QuestContextId == questContextId)
            .Select(link => link.CompetitionId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    /// <summary>
    /// The point value a competition awards for one event. A competition whose shipped value for
    /// this kind is zero still resolves to zero rather than being skipped, so a caller can tell
    /// "this event does not score here" from "this event was not eligible".
    /// </summary>
    public static int GetPointDelta(FactionScoringGameData gameData, uint competitionId, FactionCompetitionEventKind kind)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var competition = gameData.GetCompetition(competitionId);
        return kind switch
        {
            FactionCompetitionEventKind.NpcKill => competition.NpcKillPoints,
            FactionCompetitionEventKind.QuestComplete => competition.QuestCompletePoints,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown faction competition event kind.")
        };
    }

    /// <summary>
    /// Picks the winning faction from the supplied scores, and the factions the competition's reset
    /// state clears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A faction must reach <c>req_point</c> to win, except under
    /// <see cref="FactionCompetitionResetStateKind.AllIgnoreRequiredPoints"/>, where the shipped
    /// name says the threshold is ignored and score alone decides. A tie on the leading score is
    /// left unresolved: no shipped column names a tie-break.
    /// </para>
    /// <para>
    /// The two reset states that clear every faction behave identically here because both say
    /// "all"; they are still distinguished in the result so a caller can see which one ran.
    /// </para>
    /// </remarks>
    public static FactionCompetitionResolution Resolve(
        FactionScoringGameData gameData,
        uint competitionId,
        IReadOnlyDictionary<uint, long> scoresByFaction)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        ArgumentNullException.ThrowIfNull(scoresByFaction);

        var competition = gameData.GetCompetition(competitionId);
        var resetState = DecodeResetState(gameData, competition.PointResetId);
        var ignoreRequiredPoints = resetState == FactionCompetitionResetStateKind.AllIgnoreRequiredPoints;

        uint? winner = null;
        long best = long.MinValue;
        var tied = false;
        foreach (var entry in scoresByFaction.OrderBy(pair => pair.Key))
        {
            if (!ignoreRequiredPoints && entry.Value < competition.RequiredPoints)
                continue;
            if (entry.Value > best)
            {
                best = entry.Value;
                winner = entry.Key;
                tied = false;
            }
            else if (entry.Value == best)
            {
                tied = true;
            }
        }

        if (tied)
        {
            Logger.Warn(
                "Faction competition {0}: factions tied on {1} points; no winner is selected and no tie-break is applied",
                competitionId, best);
            winner = null;
        }

        var ordered = scoresByFaction.OrderBy(pair => pair.Key).Select(pair => pair.Key).ToArray();
        var resets = resetState == FactionCompetitionResetStateKind.WinnerOnly
            ? winner.HasValue ? [winner.Value] : Array.Empty<uint>()
            : ordered;

        return new FactionCompetitionResolution(competitionId, winner, resets, resetState);
    }
}

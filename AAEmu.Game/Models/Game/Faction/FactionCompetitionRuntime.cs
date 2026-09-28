using AAEmu.Game.GameData;

using NLog;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// Owns the live faction-competition score table: one score per competition per faction, plus the
/// winner and reset decision a competition's <c>point_reset_id</c> produces when it ends.
/// </summary>
/// <remarks>
/// The manager applies the shipped eligibility catalogs and point values, and publishes every
/// accepted change and every resolution so a sender or a reward path can observe them. It starts
/// no competition, arms no schedule, and grants no reward: those callers own their own timing.
/// </remarks>
public sealed class FactionCompetitionRuntime(FactionScoringGameData gameData, IFactionCompetitionRuntimeStore store = null)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new();
    private readonly Dictionary<(uint CompetitionId, uint FactionId), long> _scores = [];

    /// <summary>Raised after a faction's score in a competition changed.</summary>
    public event Action<FactionCompetitionScoreApplication> ScoreChanged;

    /// <summary>Raised when a competition's winner and reset set were resolved.</summary>
    public event Action<FactionCompetitionResolution> CompetitionResolved;

    /// <summary>
    /// Restores stored scores, dropping any whose competition or point value is no longer
    /// loadable. A score with no catalog behind it is not replayed, because nothing could award it
    /// again or resolve a winner against it. Call once at boot, after game data is loaded.
    /// </summary>
    public void Load()
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var saved = store?.LoadAll() ??
            new Dictionary<(uint CompetitionId, uint FactionId), long>();
        lock (_lock)
        {
            _scores.Clear();
            foreach (var entry in saved)
            {
                if (entry.Key.CompetitionId == 0 || entry.Key.FactionId == 0)
                    continue;
                // Refuse rather than skip quietly: a stored row naming a competition the catalog
                // does not have means the content moved under the state.
                gameData.GetCompetition(entry.Key.CompetitionId);
                _scores[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>
    /// The competitions an NPC kill counts for, and the score each awarded. Returns nothing when
    /// the NPC is not listed in any competition, which is the normal case.
    /// </summary>
    public IReadOnlyList<FactionCompetitionScoreApplication> RegisterNpcKill(uint factionId, uint npcTemplateId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var competitions = FactionCompetitionRules.CompetitionsForNpcKill(gameData, npcTemplateId);
        if (competitions.Count == 0)
            return [];

        return ApplyAll(factionId, competitions, FactionCompetitionEventKind.NpcKill, npcTemplateId);
    }

    /// <summary>
    /// The competitions a quest completion counts for, and the score each awarded. Returns nothing
    /// when the quest context is not listed in any competition.
    /// </summary>
    public IReadOnlyList<FactionCompetitionScoreApplication> RegisterQuestComplete(uint factionId, uint questContextId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        var competitions = FactionCompetitionRules.CompetitionsForQuestComplete(gameData, questContextId);
        if (competitions.Count == 0)
            return [];

        return ApplyAll(factionId, competitions, FactionCompetitionEventKind.QuestComplete, questContextId);
    }

    /// <summary>
    /// Applies one event to every competition the catalog makes it eligible for, and publishes each
    /// change. A competition whose shipped point value for this event is zero is applied as a
    /// no-op rather than skipped, so an eligible event that scores nothing is still observable.
    /// </summary>
    public IReadOnlyList<FactionCompetitionScoreApplication> ApplyAll(
        uint factionId,
        IReadOnlyList<uint> competitionIds,
        FactionCompetitionEventKind kind,
        uint sourceId)
    {
        ArgumentNullException.ThrowIfNull(competitionIds);
        var applications = new List<FactionCompetitionScoreApplication>(competitionIds.Count);
        foreach (var competitionId in competitionIds)
            applications.Add(ApplyOne(factionId, competitionId, kind, sourceId));
        return applications;
    }

    /// <summary>Applies one event to one explicitly selected competition.</summary>
    public FactionCompetitionScoreApplication ApplyOne(
        uint factionId,
        uint competitionId,
        FactionCompetitionEventKind kind,
        uint sourceId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        if (factionId == 0)
            throw new InvalidOperationException("Faction competition scoring requires a non-zero faction id.");

        var delta = FactionCompetitionRules.GetPointDelta(gameData, competitionId, kind);
        FactionCompetitionScoreApplication application;
        lock (_lock)
        {
            var key = (competitionId, factionId);
            var previous = _scores.GetValueOrDefault(key);
            long next;
            try
            {
                next = checked(previous + delta);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    $"Faction competition {competitionId} score overflow for faction {factionId} while applying {delta}.",
                    exception);
            }

            _scores[key] = next;
            application = new FactionCompetitionScoreApplication(
                competitionId, factionId, previous, next, delta, delta);
        }

        Persist(competitionId, factionId);
        Logger.Debug(
            "Faction competition {0}: faction {1} scored {2} from {3} {4}, now {5} (required {6})",
            competitionId, factionId, delta, kind, sourceId, application.Score,
            gameData.GetCompetition(competitionId).RequiredPoints);
        ScoreChanged?.Invoke(application);
        return application;
    }

    /// <summary>The faction's current score in a competition; zero when it has never scored.</summary>
    public long GetScore(uint competitionId, uint factionId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        gameData.GetCompetition(competitionId);
        lock (_lock)
            return _scores.GetValueOrDefault((competitionId, factionId));
    }

    /// <summary>Every faction that has scored in a competition, with its score.</summary>
    public IReadOnlyDictionary<uint, long> GetScores(uint competitionId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        gameData.GetCompetition(competitionId);
        lock (_lock)
            return ScoresLocked(competitionId);
    }

    private Dictionary<uint, long> ScoresLocked(uint competitionId) =>
        _scores
            .Where(entry => entry.Key.CompetitionId == competitionId)
            .OrderBy(entry => entry.Key.FactionId)
            .ToDictionary(entry => entry.Key.FactionId, entry => entry.Value);

    /// <summary>
    /// Resolves a competition's winner from the live scores and clears the factions its reset state
    /// names, publishing the resolution. Returns the resolution, whose
    /// <see cref="FactionCompetitionResolution.WinnerFactionId"/> is null when nobody won.
    /// </summary>
    public FactionCompetitionResolution ResolveAndReset(uint competitionId)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        FactionCompetitionResolution resolution;
        lock (_lock)
        {
            var resolutionScores = ScoresLocked(competitionId);
            resolution = FactionCompetitionRules.Resolve(gameData, competitionId, resolutionScores);
            foreach (var factionId in resolution.ResetFactionIds)
                _scores.Remove((competitionId, factionId));
        }

        if (store != null)
        {
            foreach (var factionId in resolution.ResetFactionIds)
                store.Delete(competitionId, factionId);
        }

        CompetitionResolved?.Invoke(resolution);
        return resolution;
    }

    private void Persist(uint competitionId, uint factionId)
    {
        if (store == null)
            return;
        long score;
        lock (_lock)
            score = _scores.GetValueOrDefault((competitionId, factionId));
        store.Save(competitionId, factionId, score);
    }
}

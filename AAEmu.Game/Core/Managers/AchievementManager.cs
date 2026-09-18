using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Achievement.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Keeps each character's achievements in step with the record values they watch, and tells the client.
/// </summary>
/// <remarks>
/// <para>
/// The client sends no achievement request — the list is server-pushed — so this manager owns three things:
/// the initial list at world entry, a change packet whenever an amount moves, and a completion packet when an
/// achievement is earned.
/// </para>
/// <para>
/// Nothing here decides what a gameplay event is worth; events report record values
/// (<see cref="CharacterRecords.Report"/>) and this manager re-evaluates only the achievements whose
/// objectives watch that record. Completing an achievement is itself a record of kind
/// <see cref="CharRecordKind.CompleteAchievement"/>, so a completion is reported like any other and the
/// achievements built on top of it (the parent/child chains) follow on their own.
/// </para>
/// </remarks>
public class AchievementManager : Singleton<AchievementManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Achievements per <c>SCAchievementsPacket</c>. The client does not accept a longer list in one packet,
    /// so a list is split into as many packets as it takes.
    /// </summary>
    public const int MaxEntriesPerPacket = 50;

    /// <summary>What one achievement's re-evaluation did, so a report can log and count it.</summary>
    public readonly record struct RefreshResult(bool AmountChanged, bool NewlyCompleted);

    /// <summary>
    /// The list to send a client: one row per achievement the character has touched, in id order.
    /// </summary>
    /// <param name="character">Whose progress to report.</param>
    /// <param name="includeUntouched">
    /// Also list achievements with no progress at all. The client starts every achievement at nothing, so the
    /// entry path only sends what it has; this is for a forced full refresh.
    /// </param>
    public List<AchievementInfo> BuildList(Character character, bool includeUntouched = false)
    {
        var list = new List<AchievementInfo>();
        if (character == null)
            return list;

        foreach (var achievement in AchievementGameData.Instance.AllAchievements)
        {
            var amount = character.Achievements.Amount(achievement.Id);
            var complete = character.Achievements.IsComplete(achievement.Id);
            if (!includeUntouched && amount == 0 && !complete)
                continue;

            list.Add(new AchievementInfo
            {
                Id = achievement.Id,
                Amount = (uint)Math.Max(0, amount),
                Complete = complete
                    ? character.Achievements.CompletedAt(achievement.Id) ?? DateTime.UtcNow
                    : default
            });
        }

        return list;
    }

    /// <summary>Sends a character their achievement list, split into packets the client accepts.</summary>
    /// <returns>How many packets were sent.</returns>
    public int SendList(Character character, bool includeUntouched = false)
    {
        if (character == null)
            return 0;

        var list = BuildList(character, includeUntouched);
        for (var offset = 0; offset < list.Count; offset += MaxEntriesPerPacket)
        {
            var count = Math.Min(MaxEntriesPerPacket, list.Count - offset);
            character.SendPacket(new SCAchievementsPacket(list.GetRange(offset, count)));
        }

        return (list.Count + MaxEntriesPerPacket - 1) / MaxEntriesPerPacket;
    }

    /// <summary>
    /// Re-evaluates every achievement against the character's records without sending anything, which is what
    /// the entry path does before it pushes the list: progress the character earned before records and
    /// achievements were both being saved is turned into completion and packet rows here.
    /// </summary>
    /// <returns>How many achievements completed.</returns>
    public int RefreshAll(Character character)
    {
        if (character == null)
            return 0;

        var ids = AchievementGameData.Instance.AllAchievements.Select(achievement => achievement.Id);
        return RefreshQueue(character, ids, sendPackets: false).Completed;
    }

    /// <summary>
    /// Reports a record's new value for a character and re-evaluates the achievements that watch it.
    /// </summary>
    /// <returns>How many achievements moved.</returns>
    public int Report(Character character, uint recordId, int value, bool sendPackets = true)
    {
        if (character == null || recordId == 0)
            return 0;

        var before = character.Records.Get(recordId);
        var kept = character.Records.Report(recordId, value);
        if (kept == before)
            return 0;

        return RefreshQueue(character, AchievementGameData.Instance.GetAchievementsWatchingRecord(recordId),
            sendPackets).Moved;
    }

    /// <summary>
    /// Reports the two records the engine can fill on its own: the character's level and each ability's.
    /// </summary>
    /// <remarks>
    /// Every other record kind is a gameplay event this slice does not own, so nothing reports them yet. These
    /// two are read straight off state the character already carries, which is what makes the level and
    /// ability-level achievements work in ordinary play rather than only through the GM surface.
    /// </remarks>
    /// <returns>How many achievements moved.</returns>
    public int ReportCharacterProgress(Character character, bool sendPackets = true)
    {
        if (character == null)
            return 0;

        return ReportLevel(character, sendPackets) + ReportAbilityLevels(character, sendPackets);
    }

    /// <summary>Reports the character's level into the records that watch it.</summary>
    public int ReportLevel(Character character, bool sendPackets = true)
    {
        if (character == null)
            return 0;

        var moved = 0;
        foreach (var record in AchievementGameData.Instance.GetRecordsOfKind(CharRecordKind.CharLevel))
            moved += Report(character, record.Id, character.Level, sendPackets);

        return moved;
    }

    /// <summary>
    /// Reports each ability's level. An ability-level record names its ability in <c>value1</c>, which is the
    /// ability's own id (the two share a numbering).
    /// </summary>
    public int ReportAbilityLevels(Character character, bool sendPackets = true)
    {
        if (character?.Abilities == null)
            return 0;

        var moved = 0;
        foreach (var record in AchievementGameData.Instance.GetRecordsOfKind(CharRecordKind.AbilityLevel))
        {
            if (record.Value1 < 0 || record.Value1 > byte.MaxValue)
                continue;

            var level = character.Abilities.GetAbilityLevel((AbilityType)record.Value1);
            moved += Report(character, record.Id, level, sendPackets);
        }

        return moved;
    }

    /// <summary>
    /// Re-evaluates one achievement, storing what it found and telling the client about it.
    /// </summary>
    public RefreshResult Refresh(Character character, uint achievementId, bool sendPackets = true)
    {
        if (character == null)
            return default;

        var achievement = AchievementGameData.Instance.GetAchievement(achievementId);
        if (achievement == null)
            return default;

        var evaluation = Evaluate(character, achievement);
        var amountChanged = false;

        if (evaluation.Progress != character.Achievements.Amount(achievementId))
        {
            character.Achievements.SetAmount(achievementId, evaluation.Progress);
            amountChanged = true;
            if (sendPackets)
                character.SendPacket(new SCAchievementChangedPacket(achievementId, evaluation.Progress));
        }

        // Completion only ever happens here, and only if the rules say the target was reached. An achievement
        // that is already complete keeps the time it was first earned.
        if (!evaluation.Complete || !character.Achievements.Complete(achievementId, DateTime.UtcNow))
            return new RefreshResult(amountChanged, false);

        if (sendPackets)
            character.SendPacket(new SCAchievementCompletedPacket(achievementId));

        Logger.Info("Achievement: {0} completed '{1}' ({2})", character.Name, achievement.Name, achievementId);
        return new RefreshResult(amountChanged, true);
    }

    /// <summary>
    /// Completes an achievement outright, records and rewards aside — the GM path, and what completing by
    /// hand has to do to keep the dependent achievements honest.
    /// </summary>
    /// <returns>True when this call was the one that completed it.</returns>
    public bool Complete(Character character, uint achievementId)
    {
        if (character == null || !AchievementGameData.Instance.HasAchievement(achievementId))
            return false;

        if (!character.Achievements.Complete(achievementId, DateTime.UtcNow))
            return false;

        character.SendPacket(new SCAchievementCompletedPacket(achievementId));

        var completionRecord = AchievementGameData.Instance.GetCompletionRecord(achievementId);
        if (completionRecord != 0)
            Report(character, completionRecord, 1);

        return true;
    }

    /// <summary>
    /// Forgets an achievement — its progress and the records its objectives are read from — and tells the
    /// client to drop it.
    /// </summary>
    /// <remarks>
    /// Dropping the progress row alone would only last until the next evaluation: the records that completed
    /// the achievement still hold what they held, so the entry path's <see cref="RefreshAll"/> would put it
    /// straight back. Clearing them is what makes the reset mean anything. A record another achievement also
    /// watches is cleared with it, because it is one counter and this is a reset of it; a record the engine
    /// fills from character state (level, ability level) is reported again the next time that state is
    /// reported, which is as it should be — the character still qualifies.
    /// </remarks>
    public bool Reset(Character character, uint achievementId)
    {
        if (character == null || !AchievementGameData.Instance.HasAchievement(achievementId))
            return false;

        foreach (var objective in AchievementGameData.Instance.GetObjectives(achievementId))
            character.Records.Clear(objective.RecordId);

        character.Achievements.Reset(achievementId);
        character.SendPacket(new SCAchievementResetedPacket(achievementId, 0));
        return true;
    }

    /// <summary>Sets a record and lets the achievements watching it follow, for the GM surface.</summary>
    public int SetRecord(Character character, uint recordId, int value)
    {
        if (character == null || AchievementGameData.Instance.GetRecord(recordId) == null)
            return 0;

        character.Records.Set(recordId, value);
        return RefreshQueue(character, AchievementGameData.Instance.GetAchievementsWatchingRecord(recordId),
            sendPackets: true).Moved;
    }

    /// <summary>
    /// Re-evaluates a set of achievements and everything their completions lead to.
    /// </summary>
    /// <remarks>
    /// A completion is itself a record that other achievements watch (the parent/child chains), so this is a
    /// queue rather than one pass. A watcher that was already passed over earlier in the pass is looked at
    /// again when a completion it watches lands: the content is not ordered by id, and 2,173 of its 3,259
    /// completion links run from a lower parent id to a higher child, so the parent is very often checked
    /// before the child that would have satisfied it. It still terminates — an achievement completes once
    /// however many ways it is reached, and only a completion re-queues anything.
    /// </remarks>
    private (int Moved, int Completed) RefreshQueue(Character character, IEnumerable<uint> achievementIds,
        bool sendPackets)
    {
        var pending = new Queue<uint>(achievementIds);
        var visited = new HashSet<uint>();
        var moved = 0;
        var completed = 0;

        while (pending.Count > 0)
        {
            var achievementId = pending.Dequeue();
            if (!visited.Add(achievementId))
                continue;

            var result = Refresh(character, achievementId, sendPackets);
            if (result.AmountChanged || result.NewlyCompleted)
                moved++;
            if (!result.NewlyCompleted)
                continue;

            completed++;

            var completionRecord = AchievementGameData.Instance.GetCompletionRecord(achievementId);
            if (completionRecord == 0)
                continue;

            character.Records.Report(completionRecord, 1);
            foreach (var watcher in AchievementGameData.Instance.GetAchievementsWatchingRecord(completionRecord))
            {
                visited.Remove(watcher);
                pending.Enqueue(watcher);
            }
        }

        return (moved, completed);
    }

    private static AchievementEvaluation Evaluate(Character character, Achievements achievement)
    {
        var objectives = AchievementGameData.Instance.GetObjectives(achievement.Id);
        var rules = new AchievementObjective[objectives.Count];
        for (var i = 0; i < objectives.Count; i++)
            rules[i] = new AchievementObjective(objectives[i].Id, objectives[i].RecordId);

        // complete_num is a count of objectives or a total of their values (see AchievementRules), and the
        // largest the content asks for is 230,000; the clamp is for safety only, so a bad value cannot wrap.
        var required = (int)Math.Min(achievement.CompleteNum, int.MaxValue);
        return AchievementRules.Evaluate(required, achievement.CompleteOr, rules, character.Records.Get);
    }
}

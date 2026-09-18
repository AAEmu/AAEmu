using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Achievement.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
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

        // Objectives can be full while a prerequisite is still missing; progress is stored, completion waits.
        if (evaluation.Complete &&
            !AchievementRules.PrerequisitesMet(
                AchievementGameData.Instance.GetPrerequisites(achievementId),
                character.Achievements.IsComplete))
            evaluation = evaluation with { Complete = false };

        // Completion only ever happens here, and only if the rules say the target was reached. An achievement
        // that is already complete keeps the time it was first earned, and the reward that came with it.
        if (!evaluation.Complete || !character.Achievements.Complete(achievementId, DateTime.UtcNow))
            return new RefreshResult(amountChanged, false);

        if (sendPackets)
            character.SendPacket(new SCAchievementCompletedPacket(achievementId));

        PayReward(character, achievement);

        Logger.Info("Achievement: {0} completed '{1}' ({2})", character.Name, achievement.Name, achievementId);
        return new RefreshResult(amountChanged, true);
    }

    /// <summary>
    /// Hands over what an achievement is worth: its item, to the bag or by mail, and its title.
    /// </summary>
    /// <remarks>
    /// Called once per character per achievement, because completion is one-way — a character who already has
    /// it cannot be paid again by a later evaluation, and a restart does not re-pay what the database says was
    /// earned before.
    /// </remarks>
    /// <returns>Whether anything was paid.</returns>
    public bool PayReward(Character character, Achievements achievement)
    {
        if (character == null || achievement == null)
            return false;

        var reward = AchievementRewardRules.RewardOf(achievement);
        var paid = false;

        if (reward.HasItem)
        {
            if (TryPayItem(character, achievement, reward, out var byMail))
            {
                character.SendPacket(new SCAchievementItemSentPacket(achievement.Id, byMail));
                Logger.Info("Achievement: {0} paid {1}x{2} for '{3}' ({4}){5}",
                    character.Name, reward.ItemCount, reward.ItemId, achievement.Name, achievement.Id,
                    byMail ? " by mail" : "");
                paid = true;
            }
            else
            {
                Logger.Warn("Achievement: {0} could not be paid item {1}x{2} for '{3}' ({4})",
                    character.Name, reward.ItemCount, reward.ItemId, achievement.Name, achievement.Id);
            }
        }

        if (reward.HasAppellation && character.Appellations != null)
        {
            character.Appellations.Add(reward.AppellationId);
            paid = true;
        }

        return paid;
    }

    /// <summary>
    /// The task type a reward item is put in the bag under.
    /// </summary>
    /// <remarks>
    /// The client has no task type for achievement rewards that this tree has identified, and the neutral one
    /// is not usable here: a container publishes nothing to the client for <c>Invalid</c>, so the item would
    /// arrive server-side and never appear in the player's bag. This is the type the other system-reward path
    /// uses for the same shape; the achievement's own "your item was sent" packet carries the context.
    /// </remarks>
    private const ItemTaskType RewardItemTask = ItemTaskType.SkillEffectGainItem;

    private static bool TryPayItem(Character character, Achievements achievement, AchievementReward reward,
        out bool byMail)
    {
        byMail = false;
        var bag = character.Inventory?.Bag;
        if (bag == null)
            return false;

        if (!AchievementRewardRules.GoesToMail(bag.SpaceLeftForItem(reward.ItemId), reward.ItemCount))
            return bag.AcquireDefaultItem(RewardItemTask, reward.ItemId, reward.ItemCount);

        byMail = true;
        return TryMailItem(character, achievement, reward);
    }

    private static bool TryMailItem(Character character, Achievements achievement, AchievementReward reward)
    {
        var attachments = character.Inventory?.MailAttachments;
        if (attachments == null)
            return false;

        if (!attachments.AcquireDefaultItemEx(ItemTaskType.Invalid, reward.ItemId, reward.ItemCount, -1,
                out var staged, out _, character.Id))
            return false;

        var mail = new BaseMail
        {
            MailType = MailType.Promotion,
            Title = achievement.Name,
            ReceiverName = character.Name,
            Header =
            {
                // A local label for the sender, the same convention the other reward mails use.
                SenderName = ".achievement",
                ReceiverId = character.Id
            },
            Body =
            {
                Text = achievement.Summary,
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow
            }
        };

        mail.Body.Attachments.AddRange(staged);
        if (mail.Send())
            return true;

        MailDeliveryRules.TryDiscardStagedAttachments(attachments, staged);
        return false;
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
        PayReward(character, AchievementGameData.Instance.GetAchievement(achievementId));

        // The records it counts into. Prerequisites are a gate on Refresh, not a credit from this force.
        foreach (var recordId in CompletionRecords(character, achievementId))
            Report(character, recordId, 1);

        // Gated rows do not watch this completion record (2052 has none), so they are refreshed by name.
        RefreshQueue(character, AchievementGameData.Instance.GetGatedByPrerequisite(achievementId),
            sendPackets: true);

        return true;
    }

    /// <summary>
    /// Forgets an achievement — its progress and the records its objectives are read from — and tells the
    /// client to drop it.
    /// </summary>
    /// <remarks>
    /// Dropping the progress row alone would only last until the next evaluation: the records that completed
    /// the achievement still hold what they held, so the entry path's <see cref="RefreshAll"/> would put it
    /// straight back. Clearing them is what makes the reset mean anything. A completion record whose
    /// achievement is still complete is kept: 674 achievements watch those, and a complete child never
    /// reports again, so clearing it would make the parent unearnable. A sub-category record whose
    /// sub-category is still complete is kept the same way: that record is only reported when a member
    /// newly completes, so clearing a payer's only objective would make the payer unearnable. A record the
    /// engine fills from character state (level, ability level) is reported again the next time that state
    /// is reported.
    /// </remarks>
    public bool Reset(Character character, uint achievementId)
    {
        if (character == null || !AchievementGameData.Instance.HasAchievement(achievementId))
            return false;

        foreach (var objective in AchievementGameData.Instance.GetObjectives(achievementId))
        {
            var completedId = AchievementGameData.Instance.GetAchievementForCompletionRecord(objective.RecordId);
            if (completedId != 0 && character.Achievements.IsComplete(completedId))
                continue;

            var subCategoryId = AchievementGameData.Instance.GetSubCategoryForRecord(objective.RecordId);
            if (subCategoryId != 0 && SubCategoryIsComplete(character, subCategoryId))
                continue;

            character.Records.Clear(objective.RecordId);
        }

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

            // A completion is a record of its own, and finishing a whole sub-category is another one; both
            // are reported here so the achievements built on them follow through the same queue.
            foreach (var recordId in CompletionRecords(character, achievementId))
            {
                character.Records.Report(recordId, 1);
                foreach (var watcher in AchievementGameData.Instance.GetAchievementsWatchingRecord(recordId))
                {
                    // A watcher passed over earlier in this pass has to be looked at again: the content is
                    // not ordered by id, so a parent is very often checked before the child that completes
                    // it, and the completion is what should have satisfied the parent.
                    visited.Remove(watcher);
                    pending.Enqueue(watcher);
                }
            }

            foreach (var gatedId in AchievementGameData.Instance.GetGatedByPrerequisite(achievementId))
            {
                visited.Remove(gatedId);
                pending.Enqueue(gatedId);
            }
        }

        return (moved, completed);
    }

    /// <summary>
    /// The records an achievement's completion counts into: the one naming the achievement, and — once every
    /// achievement of its sub-category is done — the one naming the sub-category.
    /// </summary>
    /// <remarks>
    /// The achievement that the sub-category record is for is itself filed under that sub-category, so it is
    /// left out of the count: it is what finishing the sub-category pays, not part of finishing it. So are the
    /// achievements the season has switched off, which cannot be earned at all — 34 of the 44 sub-categories
    /// hold at least one — and members with no objectives (47 unused/test rows), which nothing can complete.
    /// </remarks>
    private static IEnumerable<uint> CompletionRecords(Character character, uint achievementId)
    {
        var completionRecord = AchievementGameData.Instance.GetCompletionRecord(achievementId);
        if (completionRecord != 0)
            yield return completionRecord;

        var achievement = AchievementGameData.Instance.GetAchievement(achievementId);
        if (achievement == null || achievement.SubCategoryId == 0)
            yield break;

        if (!SubCategoryIsComplete(character, achievement.SubCategoryId))
            yield break;

        yield return AchievementGameData.Instance.GetSubCategoryRecord(achievement.SubCategoryId);
    }

    /// <summary>
    /// Every countable member of the sub-category is done. The payer itself, season-off rows, and members
    /// with no objectives are left out of that count — the same exclusions <see cref="CompletionRecords"/>
    /// uses when it reports the sub-category record.
    /// </summary>
    private static bool SubCategoryIsComplete(Character character, uint subCategoryId)
    {
        var subCategoryRecord = AchievementGameData.Instance.GetSubCategoryRecord(subCategoryId);
        if (subCategoryRecord == 0)
            return false;

        var members = AchievementGameData.Instance.GetSubCategoryAchievements(subCategoryId);
        if (members.Count == 0)
            return false;

        var payers = AchievementGameData.Instance.GetAchievementsWatchingRecord(subCategoryRecord);
        return members.All(memberId =>
            payers.Contains(memberId) ||
            AchievementGameData.Instance.GetAchievement(memberId)?.SeasonOff == true ||
            AchievementGameData.Instance.GetObjectives(memberId).Count == 0 ||
            character.Achievements.IsComplete(memberId));
    }

    private static AchievementEvaluation Evaluate(Character character, Achievements achievement)
    {
        var objectives = AchievementGameData.Instance.GetObjectives(achievement.Id);
        var rules = new List<AchievementObjective>(objectives.Count);
        foreach (var objective in objectives)
        {
            // An objective that watches a season-off achievement's completion can never be satisfied: it is
            // not something this achievement waits for.
            if (AchievementGameData.Instance.IsSeasonOffCompletionRecord(objective.RecordId))
                continue;

            rules.Add(new AchievementObjective(objective.Id, objective.RecordId));
        }

        // complete_num is a count of objectives or a total of their values (see AchievementRules), and the
        // largest the content asks for is 2,000,000 (achievement 2539, a my_gold total; 2244 asks 1,000,000
        // and 2538 asks 500,000 behind it); the clamp is for safety only, so a bad value cannot wrap.
        var required = (int)Math.Min(achievement.CompleteNum, int.MaxValue);
        return AchievementRules.Evaluate(required, achievement.CompleteOr, rules, character.Records.Get);
    }
}

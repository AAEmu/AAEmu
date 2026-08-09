using System.Globalization;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.TodayAssignment;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Daily schedule (today_quest_* tables).
/// Client visuals for Locked: grey until level+cost item (client); green when satisfy.
/// Flow: LOCKED → Unlock → READY (blue, no quest) → Accept → PROGRESS → DONE.
/// Paid steps (item cost): unlock once per character; each new day seeds READY again.
/// </summary>
public class TodayAssignmentManager : Singleton<TodayAssignmentManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const sbyte RequestQuery = 0;
    private const sbyte RequestUnlock = 1;
    private const sbyte RequestAccept = 2;

    /// <summary>Free mission re-rolls allowed per calendar day.</summary>
    private const uint MaxDailyResets = 3;

    /// <summary>SCTodayAssignmentResetCount countType for personal daily schedule.</summary>
    private const uint PersonalResetCountType = 0x0B;

    /// <summary>characterId → realStep → assignment (today only).</summary>
    private readonly Dictionary<uint, Dictionary<uint, ActiveTodayAssignment>> _active = [];

    /// <summary>characterId → free re-rolls used today (0..MaxDailyResets).</summary>
    private readonly Dictionary<uint, uint> _resetsUsed = [];

    /// <summary>
    /// Paid slot unlocks that survive across days (item cost paid once per character).
    /// Free steps (no item cost) are not stored here.
    /// </summary>
    private readonly Dictionary<uint, HashSet<uint>> _lifetimeUnlocks = [];

    /// <summary>
    /// Characters loaded for a specific local calendar day. If the day changes while online,
    /// EnsureLoaded reloads so assignment/reroll state does not carry across midnight.
    /// </summary>
    private readonly Dictionary<uint, DateTime> _loadedDay = [];

    private sealed class ActiveTodayAssignment
    {
        public uint GroupId { get; set; }
        public uint QuestContextId { get; set; }
        public bool Unlocked { get; set; }
        public bool Accepted { get; set; }
        public TodayAssignmentStatus Status { get; set; }
    }

    private static DateTime TodayKey => DateTime.Today;

    private static bool IsPaidStep(TodayQuestStepTemplate step)
        => step != null && step.ItemId != 0 && step.ItemNum > 0;

    public void SendResetCount(Character character)
    {
        if (character == null)
            return;

        var used = _resetsUsed.GetValueOrDefault(character.Id, 0u);
        character.SendPacket(new SCTodayAssignmentResetCountPacket(used, PersonalResetCountType));
    }

    /// <summary>
    /// Load today's assignments from MySQL and push SC to the client (NotifyInGame).
    /// </summary>
    public void OnCharacterEnterWorld(Character character)
    {
        if (character == null)
            return;

        EnsureLoaded(character);
        SendResetCount(character);

        if (!TryGetCharState(character.Id, out var byStep) || byStep.Count == 0)
            return;

        foreach (var (realStep, state) in byStep)
        {
            // Do not re-fire complete-toast on login.
            SendState(character, realStep, state, init: true);
            if (state.Status == TodayAssignmentStatus.Done)
                DropSiblingQuests(character, state.GroupId, keepQuestId: state.QuestContextId);
        }
    }

    public void HandleRequest(Character character, uint realStep, sbyte request)
    {
        if (character == null)
            return;

        EnsureLoaded(character);

        Logger.Info(
            "TodayAssignment request {0} ({1}) realStep={2} request={3}",
            character.Name, character.Id, realStep, request);

        var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
        if (step == null)
        {
            Logger.Warn("TodayAssignment: unknown realStep={0} for {1}", realStep, character.Name);
            return;
        }

        if (!MeetsStepEligibility(character, step, realStep, log: true))
            return;

        switch (request)
        {
            case RequestQuery:
                Resync(character, realStep);
                SendResetCount(character);
                break;
            case RequestUnlock:
                Unlock(character, step, realStep);
                break;
            case RequestAccept:
                Accept(character, step, realStep);
                break;
            default:
                Logger.Warn(
                    "TodayAssignment: unhandled request={0} realStep={1} for {2}",
                    request, realStep, character.Name);
                break;
        }
    }

    /// <summary>
    /// Change Mission: re-roll an in-progress step. Free while under MaxDailyResets;
    /// charges inventory gold when moneyAmount is greater than zero.
    /// </summary>
    public void HandleReset(Character character, uint realStep, ulong moneyAmount)
    {
        if (character == null)
            return;

        EnsureLoaded(character);

        Logger.Info(
            "TodayAssignment reset {0} ({1}) realStep={2} money={3}",
            character.Name, character.Id, realStep, moneyAmount);

        var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
        if (step == null)
        {
            Logger.Warn("TodayAssignment: reset unknown realStep={0} for {1}", realStep, character.Name);
            return;
        }

        if (!TryGetActive(character.Id, realStep, out var state)
            || !state.Accepted
            || state.Status != TodayAssignmentStatus.Progress)
        {
            Logger.Info(
                "TodayAssignment: reset refused (not in Progress) realStep={0} for {1}",
                realStep, character.Name);
            return;
        }

        var used = _resetsUsed.GetValueOrDefault(character.Id, 0u);
        if (used >= MaxDailyResets)
        {
            Logger.Info(
                "TodayAssignment: reset limit reached ({0}/{1}) for {2}",
                used, MaxDailyResets, character.Name);
            SendResetCount(character);
            return;
        }

        if (moneyAmount > 0)
        {
            if (moneyAmount > (ulong)long.MaxValue
                || !character.ChangeMoney(
                    SlotType.Inventory,
                    SlotType.None,
                    (long)moneyAmount,
                    ItemTaskType.StoreBuy))
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                return;
            }
        }

        var oldGroupId = state.GroupId;
        var oldQuestId = state.QuestContextId;

        if (!TryPickReplacement(character, step, oldGroupId, oldQuestId, out var newGroup, out var newQuestId))
        {
            Logger.Warn(
                "TodayAssignment: reset no alternate quest realStep={0} for {1} (group={2} quest={3})",
                realStep, character.Name, oldGroupId, oldQuestId);
            // Refund gold if we charged but cannot re-roll.
            if (moneyAmount > 0)
                character.ChangeMoney(SlotType.None, SlotType.Inventory, (long)moneyAmount, ItemTaskType.StoreBuy);
            return;
        }

        // Drop every active quest in the old group, then start the replacement.
        DropSiblingQuests(character, oldGroupId, keepQuestId: 0);
        if (newGroup.Id != oldGroupId)
            DropSiblingQuests(character, newGroup.Id, keepQuestId: 0);

        if (!character.Quests.AddQuest(newQuestId))
        {
            Logger.Warn(
                "TodayAssignment: reset failed to start quest={0} realStep={1} for {2}",
                newQuestId, realStep, character.Name);
            if (moneyAmount > 0)
                character.ChangeMoney(SlotType.None, SlotType.Inventory, (long)moneyAmount, ItemTaskType.StoreBuy);
            // Try to restore the previous contract if it still exists as a template.
            if (oldQuestId != 0 && !character.Quests.HasQuestCompleted(oldQuestId))
                character.Quests.AddQuest(oldQuestId);
            return;
        }

        state.GroupId = newGroup.Id;
        state.QuestContextId = newQuestId;
        state.Unlocked = true;
        state.Accepted = true;
        state.Status = TodayAssignmentStatus.Progress;
        SetActive(character.Id, realStep, state);
        Persist(character.Id, realStep, state);

        var newUsed = used + 1;
        _resetsUsed[character.Id] = newUsed;
        PersistResetsUsed(character.Id, newUsed);

        SendState(character, realStep, state, init: false);
        SendResetCount(character);

        Logger.Info(
            "TodayAssignment reset ok {0}: realStep={1} group {2}→{3} quest {4}→{5} used={6}/{7}",
            character.Name, realStep, oldGroupId, newGroup.Id, oldQuestId, newQuestId,
            newUsed, MaxDailyResets);
    }

    public void HandleAcceptAll(Character character, sbyte todayType, IReadOnlyList<uint> realSteps)
    {
        if (character == null)
            return;

        EnsureLoaded(character);

        Logger.Info(
            "TodayAssignment AcceptAll {0} ({1}) todayType={2} steps=[{3}]",
            character.Name, character.Id, todayType, string.Join(",", realSteps ?? []));

        var ok = true;
        if (realSteps == null || realSteps.Count == 0)
        {
            if (TryGetCharState(character.Id, out var byStep))
            {
                foreach (var (realStep, state) in byStep.ToList())
                {
                    if (state.Unlocked && !state.Accepted && state.Status != TodayAssignmentStatus.Done)
                    {
                        var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
                        if (step == null
                            || !MeetsStepEligibility(character, step, realStep, log: true)
                            || !Accept(character, step, realStep))
                            ok = false;
                    }
                }
            }
        }
        else
        {
            foreach (var realStep in realSteps)
            {
                var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
                if (step == null)
                {
                    ok = false;
                    continue;
                }

                // Same step-level gates as CSRequestTodayAssignment — bulk list is client-controlled.
                if (!MeetsStepEligibility(character, step, realStep, log: true))
                {
                    ok = false;
                    continue;
                }

                if (!TryGetActive(character.Id, realStep, out var state) || !state.Unlocked)
                    Unlock(character, step, realStep);

                if (!Accept(character, step, realStep))
                    ok = false;
            }
        }

        character.SendPacket(new SCTodayAssignmentAcceptAllPacket(ok ? (ushort)0 : (ushort)1));
    }

    public void OnCharacterLogout(uint characterId)
    {
        _active.Remove(characterId);
        _resetsUsed.Remove(characterId);
        _lifetimeUnlocks.Remove(characterId);
        _loadedDay.Remove(characterId);
    }

    /// <summary>
    /// Step unit_reqs + level bounds shared by single-step requests and AcceptAll bulk unlock.
    /// </summary>
    private static bool MeetsStepEligibility(
        Character character,
        TodayQuestStepTemplate step,
        uint realStep,
        bool log)
    {
        if (!UnitRequirementsGameData.Instance.MeetOwnerRequirements(
                "TodayQuestStep", step.Id, step.OrUnitReqs, character))
        {
            if (log)
            {
                Logger.Info(
                    "TodayAssignment: step reqs failed realStep={0} stepId={1} for {2}",
                    realStep, step.Id, character.Name);
            }

            return false;
        }

        if (step.LevelMin > 0 && character.Level < step.LevelMin)
            return false;
        if (step.LevelMax > 0 && character.Level > step.LevelMax)
            return false;

        return true;
    }

    public void NotifyQuestCompleted(Character character, uint questContextId)
    {
        if (character == null || questContextId == 0)
            return;

        EnsureLoaded(character);

        if (!TryGetCharState(character.Id, out var byStep) || byStep.Count == 0)
        {
            TryCompleteFromQuestId(character, questContextId);
            return;
        }

        foreach (var (realStep, state) in byStep.ToList())
        {
            if (state.Status == TodayAssignmentStatus.Done)
                continue;
            if (!state.Accepted && !state.Unlocked)
                continue;

            var group = TodayQuestGameData.Instance.GetGroup(state.GroupId);
            if (group == null)
                continue;

            if (!group.QuestContextIds.Contains(questContextId)
                && state.QuestContextId != questContextId)
                continue;

            CompleteStep(character, realStep, state, questContextId);
            return;
        }

        TryCompleteFromQuestId(character, questContextId);
    }

    private void TryCompleteFromQuestId(Character character, uint questContextId)
    {
        var group = TodayQuestGameData.Instance.FindGroupByQuestId(questContextId);
        if (group == null)
            return;

        var step = TodayQuestGameData.Instance.GetStepById(group.StepId);
        if (step == null)
            return;

        if (!TryGetActive(character.Id, step.RealStep, out var state))
        {
            state = new ActiveTodayAssignment
            {
                GroupId = group.Id,
                Unlocked = true,
                Accepted = true
            };
            SetActive(character.Id, step.RealStep, state);
        }
        else
        {
            state.GroupId = group.Id;
        }

        CompleteStep(character, step.RealStep, state, questContextId);
    }

    private void CompleteStep(
        Character character,
        uint realStep,
        ActiveTodayAssignment state,
        uint completedQuestId)
    {
        state.GroupId = state.GroupId != 0
            ? state.GroupId
            : TodayQuestGameData.Instance.FindGroupByQuestId(completedQuestId)?.Id ?? 0;
        state.QuestContextId = completedQuestId;
        state.Unlocked = true;
        state.Accepted = true;
        state.Status = TodayAssignmentStatus.Done;
        SetActive(character.Id, realStep, state);
        Persist(character.Id, realStep, state);

        SendState(character, realStep, state, init: false);
        DropSiblingQuests(character, state.GroupId, keepQuestId: completedQuestId);

        Logger.Info(
            "TodayAssignment done {0}: realStep={1} group={2} quest={3}",
            character.Name, realStep, state.GroupId, completedQuestId);
    }

    private void Unlock(Character character, TodayQuestStepTemplate step, uint realStep)
    {
        if (TryGetActive(character.Id, realStep, out var existing) && existing.Unlocked)
        {
            // Done/progress for today must not be reset to Ready by a re-unlock click.
            if (existing.Status == TodayAssignmentStatus.Done)
            {
                DropSiblingQuests(character, existing.GroupId, keepQuestId: existing.QuestContextId);
                SendState(character, realStep, existing, init: false);
                return;
            }

            // Re-click on an unlocked, not-yet-accepted slot completes accept (no second charge).
            if (existing.Status == TodayAssignmentStatus.Ready || !existing.Accepted)
            {
                Accept(character, step, realStep);
                return;
            }

            SendState(character, realStep, existing, init: false);
            return;
        }

        var group = PickEligibleGroup(character, step);
        if (group == null)
        {
            Logger.Warn(
                "TodayAssignment: unlock no eligible group realStep={0} for {1} (level={2} race={3})",
                realStep, character.Name, character.Level, character.Race);
            return;
        }

        // Green unlock → blue Ready only (no quest start). Item cost once for paid slots.
        if (IsPaidStep(step))
        {
            if (!HasLifetimeUnlock(character.Id, realStep))
            {
                if (!TryConsumeUnlockCost(character, step, realStep))
                    return;
                GrantLifetimeUnlock(character.Id, realStep);
            }
        }

        var state = new ActiveTodayAssignment
        {
            GroupId = group.Id,
            QuestContextId = 0,
            Unlocked = true,
            Accepted = false,
            Status = TodayAssignmentStatus.Ready
        };
        SetActive(character.Id, realStep, state);
        Persist(character.Id, realStep, state);

        Logger.Info(
            "TodayAssignment unlocked→Ready {0}: realStep={1} group={2} costItem={3}x{4} lifetime={5}",
            character.Name, realStep, group.Id, step.ItemId, step.ItemNum,
            IsPaidStep(step) && HasLifetimeUnlock(character.Id, realStep));

        SendState(character, realStep, state, init: false);
        SendResetCount(character);
    }

    /// <summary>
    /// Consumes today_quest_steps item cost from bag inventory when unlocking a paid step.
    /// </summary>
    private static bool TryConsumeUnlockCost(Character character, TodayQuestStepTemplate step, uint realStep)
    {
        if (step.ItemId == 0 || step.ItemNum <= 0)
            return true;

        if (!character.Inventory.CheckItems(SlotType.Inventory, step.ItemId, step.ItemNum))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            Logger.Info(
                "TodayAssignment: unlock cost missing {0} realStep={1} item={2} need={3}",
                character.Name, realStep, step.ItemId, step.ItemNum);
            return false;
        }

        var consumed = character.Inventory.Bag.ConsumeItem(
            ItemTaskType.Destroy,
            step.ItemId,
            step.ItemNum,
            null);
        if (consumed < step.ItemNum)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            Logger.Warn(
                "TodayAssignment: unlock consume short {0} realStep={1} item={2} need={3} got={4}",
                character.Name, realStep, step.ItemId, step.ItemNum, consumed);
            return false;
        }

        Logger.Info(
            "TodayAssignment: unlock consumed {0}×{1} from {2} realStep={3}",
            step.ItemId, step.ItemNum, character.Name, realStep);
        return true;
    }

    private bool Accept(Character character, TodayQuestStepTemplate step, uint realStep)
    {
        // Not unlocked yet: unlock only (Ready/blue). Client must click Accept/unlock again for Progress.
        if (!TryGetActive(character.Id, realStep, out var state) || !state.Unlocked)
        {
            Unlock(character, step, realStep);
            return TryGetActive(character.Id, realStep, out state)
                   && state.Unlocked
                   && state.Status == TodayAssignmentStatus.Ready;
        }

        if (state.Status == TodayAssignmentStatus.Done)
        {
            DropSiblingQuests(character, state.GroupId, keepQuestId: state.QuestContextId);
            SendState(character, realStep, state, init: false);
            return true;
        }

        if (state.Accepted)
        {
            if (state.QuestContextId != 0 && state.Status < TodayAssignmentStatus.Progress)
                state.Status = TodayAssignmentStatus.Progress;
            Persist(character.Id, realStep, state);
            SendState(character, realStep, state, init: false);
            return true;
        }

        var group = TodayQuestGameData.Instance.GetGroup(state.GroupId)
                    ?? PickEligibleGroup(character, step);
        if (group == null || group.QuestContextIds.Count == 0)
        {
            Logger.Warn(
                "TodayAssignment: accept no group/quests realStep={0} for {1}",
                realStep, character.Name);
            return false;
        }

        state.GroupId = group.Id;

        // Prefer a single active difficulty: if several already exist from older bugs, keep the first.
        var startedAny = false;
        uint primaryQuestId = 0;
        foreach (var questId in group.QuestContextIds)
        {
            if (character.Quests.HasQuest(questId))
            {
                startedAny = true;
                if (primaryQuestId == 0)
                    primaryQuestId = questId;
            }
        }

        // Start only the first group quest that can still be started (repeatables ok on later days).
        if (!startedAny)
        {
            foreach (var questId in group.QuestContextIds)
            {
                if (character.Quests.AddQuest(questId))
                {
                    primaryQuestId = questId;
                    startedAny = true;
                    break;
                }
            }
        }
        else
        {
            // Drop extra active variants so progress is not split across 10/20/30 tracks.
            foreach (var questId in group.QuestContextIds)
            {
                if (questId == primaryQuestId)
                    continue;
                if (character.Quests.HasQuest(questId))
                    character.Quests.DropQuest(questId, true, false);
            }
        }

        if (!startedAny || primaryQuestId == 0)
        {
            Logger.Warn(
                "TodayAssignment: accept failed to start quests realStep={0} group={1} for {2}",
                realStep, group.Id, character.Name);
            return false;
        }

        state.QuestContextId = primaryQuestId;
        state.Accepted = true;
        state.Status = TodayAssignmentStatus.Progress;
        SetActive(character.Id, realStep, state);
        Persist(character.Id, realStep, state);
        SendState(character, realStep, state, init: false);
        SendResetCount(character);

        Logger.Info(
            "TodayAssignment accepted {0}: realStep={1} group={2} quest={3}",
            character.Name, realStep, group.Id, primaryQuestId);
        return true;
    }

    private void Resync(Character character, uint realStep)
    {
        if (TryGetActive(character.Id, realStep, out var state))
            SendState(character, realStep, state, init: false);
    }

    private static void DropSiblingQuests(Character character, uint groupId, uint keepQuestId)
    {
        if (groupId == 0)
            return;

        var group = TodayQuestGameData.Instance.GetGroup(groupId);
        if (group == null)
            return;

        foreach (var otherId in group.QuestContextIds)
        {
            // Drop every still-active variant once the contract is done (including the finished one
            // if DropQuest hasn't run yet — caller may remove keepQuestId after CompleteStep).
            if (character.Quests.HasQuest(otherId))
                character.Quests.DropQuest(otherId, true, false);
        }

        // Silence unused parameter if we always drop all actives for Done.
        _ = keepQuestId;
    }

    private static TodayQuestGroupTemplate PickEligibleGroup(Character character, TodayQuestStepTemplate step)
    {
        var eligible = GetEligibleGroups(character, step);
        return eligible.Count > 0 ? eligible[0] : null;
    }

    private static List<TodayQuestGroupTemplate> GetEligibleGroups(Character character, TodayQuestStepTemplate step)
    {
        var list = new List<TodayQuestGroupTemplate>();
        foreach (var group in step.Groups)
        {
            if (!UnitRequirementsGameData.Instance.MeetOwnerRequirements(
                    "TodayQuestGroup", group.Id, group.OrUnitReqs, character))
                continue;
            list.Add(group);
        }

        return list;
    }

    /// <summary>
    /// Prefer a different eligible group, then a different incomplete quest in any eligible group.
    /// Falls back to the current quest only if nothing else is available.
    /// </summary>
    private static bool TryPickReplacement(
        Character character,
        TodayQuestStepTemplate step,
        uint oldGroupId,
        uint oldQuestId,
        out TodayQuestGroupTemplate newGroup,
        out uint newQuestId)
    {
        newGroup = null;
        newQuestId = 0;

        var eligible = GetEligibleGroups(character, step);
        if (eligible.Count == 0)
            return false;

        // Candidates: (group, quest) that are incomplete and not the current pair.
        var alternates = new List<(TodayQuestGroupTemplate Group, uint QuestId)>();
        var sameAsCurrent = ((TodayQuestGroupTemplate Group, uint QuestId)?)null;

        foreach (var group in eligible)
        {
            foreach (var questId in group.QuestContextIds)
            {
                if (character.Quests.HasQuestCompleted(questId))
                    continue;

                if (group.Id == oldGroupId && questId == oldQuestId)
                {
                    sameAsCurrent = (group, questId);
                    continue;
                }

                alternates.Add((group, questId));
            }
        }

        // Prefer a different group when several are eligible.
        var otherGroups = alternates.Where(c => c.Group.Id != oldGroupId).ToList();
        var pool = otherGroups.Count > 0
            ? otherGroups
            : alternates;

        if (pool.Count == 0)
        {
            if (sameAsCurrent == null)
                return false;
            newGroup = sameAsCurrent.Value.Group;
            newQuestId = sameAsCurrent.Value.QuestId;
            return true;
        }

        var pick = pool[Random.Shared.Next(pool.Count)];
        newGroup = pick.Group;
        newQuestId = pick.QuestId;
        return true;
    }

    private void EnsureLoaded(Character character)
    {
        if (character == null)
            return;

        var today = TodayKey;
        if (_loadedDay.TryGetValue(character.Id, out var loadedDay) && loadedDay == today)
            return;

        // Midnight crossed while still online (or first load this process).
        var dayRolledOver = loadedDay != default && loadedDay != today;
        if (dayRolledOver)
        {
            Logger.Info(
                "TodayAssignment day rollover owner={0} was={1:yyyy-MM-dd} now={2:yyyy-MM-dd}",
                character.Id, loadedDay, today);
            _active.Remove(character.Id);
            _resetsUsed.Remove(character.Id);
        }

        LoadFromDb(character);
        _loadedDay[character.Id] = today;

        // Refresh client UI so yesterday's Done/Progress does not stick after midnight.
        if (dayRolledOver)
            PushDayRolloverUi(character);
    }

    private void PushDayRolloverUi(Character character)
    {
        SendResetCount(character);

        foreach (var step in TodayQuestGameData.Instance.AllSteps)
        {
            if (TryGetActive(character.Id, step.RealStep, out var state))
            {
                SendState(character, step.RealStep, state, init: true);
                continue;
            }

            character.SendPacket(new SCTodayAssignmentChangedPacket(
                (int)step.Id,
                0,
                0,
                (sbyte)TodayAssignmentStatus.Locked,
                init: true));
        }
    }

    private void LoadFromDb(Character character)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT real_step, group_id, quest_context_id, status, day_key
                FROM character_today_assignments
                WHERE owner = @owner
                """;
            command.Parameters.AddWithValue("@owner", character.Id);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var dayKey = reader.GetDateTime("day_key").Date;
                var realStep = reader.GetUInt32("real_step");
                if (dayKey != TodayKey)
                {
                    // Stale day — leave row; Persist will overwrite when used; ignore for load.
                    continue;
                }

                var status = (TodayAssignmentStatus)reader.GetByte("status");
                if (status is < TodayAssignmentStatus.Ready or > TodayAssignmentStatus.Done)
                    continue;

                var state = new ActiveTodayAssignment
                {
                    GroupId = reader.GetUInt32("group_id"),
                    QuestContextId = reader.GetUInt32("quest_context_id"),
                    Status = status,
                    Unlocked = status >= TodayAssignmentStatus.Ready,
                    Accepted = status >= TodayAssignmentStatus.Progress
                };
                SetActive(character.Id, realStep, state);

                // Any paid step used today is also a lifetime unlock.
                var stepTpl = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
                if (IsPaidStep(stepTpl) && status >= TodayAssignmentStatus.Ready)
                    RememberLifetimeUnlock(character.Id, realStep);
            }
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            Logger.Warn(
                "TodayAssignment table missing — run SQL/updates/2026-08-09_aaemu_game_character_today_assignments.sql ({0})",
                ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TodayAssignment LoadFromDb failed for {0}", character.Id);
        }

        LoadLifetimeUnlocksFromDb(character);
        BackfillLifetimeUnlocksFromHistory(character);
        SeedReadyForLifetimeUnlocks(character);
        LoadResetsUsedFromDb(character);
    }

    private bool HasLifetimeUnlock(uint characterId, uint realStep)
        => _lifetimeUnlocks.TryGetValue(characterId, out var set) && set.Contains(realStep);

    private void RememberLifetimeUnlock(uint characterId, uint realStep)
    {
        if (!_lifetimeUnlocks.TryGetValue(characterId, out var set))
        {
            set = [];
            _lifetimeUnlocks[characterId] = set;
        }

        set.Add(realStep);
    }

    private void GrantLifetimeUnlock(uint characterId, uint realStep)
    {
        RememberLifetimeUnlock(characterId, realStep);
        PersistLifetimeUnlock(characterId, realStep);
    }

    private void LoadLifetimeUnlocksFromDb(Character character)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT real_step
                FROM character_today_step_unlocks
                WHERE owner = @owner
                """;
            command.Parameters.AddWithValue("@owner", character.Id);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
                RememberLifetimeUnlock(character.Id, reader.GetUInt32("real_step"));
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            Logger.Warn(
                "TodayAssignment step-unlocks table missing — run SQL/updates/2026-08-09_aaemu_game_character_today_step_unlocks.sql ({0})",
                ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TodayAssignment LoadLifetimeUnlocks failed for {0}", character.Id);
        }
    }

    /// <summary>
    /// One-time migration: any past daily row for a paid step implies the character already paid.
    /// </summary>
    private void BackfillLifetimeUnlocksFromHistory(Character character)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT DISTINCT real_step
                FROM character_today_assignments
                WHERE owner = @owner AND status >= 1
                """;
            command.Parameters.AddWithValue("@owner", character.Id);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var realStep = reader.GetUInt32("real_step");
                var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
                if (!IsPaidStep(step) || HasLifetimeUnlock(character.Id, realStep))
                    continue;

                RememberLifetimeUnlock(character.Id, realStep);
                PersistLifetimeUnlock(character.Id, realStep);
                Logger.Info(
                    "TodayAssignment backfilled lifetime unlock {0} realStep={1}",
                    character.Name, realStep);
            }
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            // assignments or unlocks table missing — ignore
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TodayAssignment BackfillLifetimeUnlocks failed for {0}", character.Id);
        }
    }

    /// <summary>
    /// After midnight / first login of the day: paid slots already unlocked sit Ready (blue), not Locked.
    /// </summary>
    private void SeedReadyForLifetimeUnlocks(Character character)
    {
        if (!_lifetimeUnlocks.TryGetValue(character.Id, out var unlockedSteps) || unlockedSteps.Count == 0)
            return;

        foreach (var realStep in unlockedSteps)
        {
            if (TryGetActive(character.Id, realStep, out _))
                continue;

            var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
            if (step == null || !IsPaidStep(step))
                continue;

            if (!MeetsStepEligibility(character, step, realStep, log: false))
                continue;

            var group = PickEligibleGroup(character, step);
            if (group == null)
                continue;

            var state = new ActiveTodayAssignment
            {
                GroupId = group.Id,
                QuestContextId = 0,
                Unlocked = true,
                Accepted = false,
                Status = TodayAssignmentStatus.Ready
            };
            SetActive(character.Id, realStep, state);
            Persist(character.Id, realStep, state);
            Logger.Info(
                "TodayAssignment seeded Ready for lifetime unlock {0} realStep={1} group={2}",
                character.Name, realStep, group.Id);
        }
    }

    private void PersistLifetimeUnlock(uint ownerId, uint realStep)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT IGNORE INTO character_today_step_unlocks (owner, real_step)
                VALUES (@owner, @realStep)
                """;
            command.Parameters.AddWithValue("@owner", ownerId);
            command.Parameters.AddWithValue("@realStep", realStep);
            command.Prepare();
            command.ExecuteNonQuery();
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            Logger.Warn(
                "TodayAssignment PersistLifetimeUnlock skipped (table missing): owner={0} realStep={1}",
                ownerId, realStep);
        }
        catch (Exception ex)
        {
            Logger.Error(
                ex, "TodayAssignment PersistLifetimeUnlock failed owner={0} realStep={1}", ownerId, realStep);
        }
    }

    private void LoadResetsUsedFromDb(Character character)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT resets_used, day_key
                FROM character_today_reset_counts
                WHERE owner = @owner
                """;
            command.Parameters.AddWithValue("@owner", character.Id);
            command.Prepare();

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                _resetsUsed.Remove(character.Id);
                return;
            }

            var dayKey = reader.GetDateTime("day_key").Date;
            if (dayKey != TodayKey)
            {
                _resetsUsed.Remove(character.Id);
                return;
            }

            var used = reader.GetUInt32("resets_used");
            if (used > MaxDailyResets)
                used = MaxDailyResets;
            _resetsUsed[character.Id] = used;
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            Logger.Warn(
                "TodayAssignment reset-count table missing — run SQL/updates/2026-08-09_aaemu_game_character_today_assignments.sql ({0})",
                ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TodayAssignment LoadResetsUsed failed for {0}", character.Id);
        }
    }

    private void Persist(uint ownerId, uint realStep, ActiveTodayAssignment state)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                REPLACE INTO character_today_assignments
                    (owner, real_step, group_id, quest_context_id, status, day_key)
                VALUES
                    (@owner, @realStep, @groupId, @questId, @status, @dayKey)
                """;
            command.Parameters.AddWithValue("@owner", ownerId);
            command.Parameters.AddWithValue("@realStep", realStep);
            command.Parameters.AddWithValue("@groupId", state.GroupId);
            command.Parameters.AddWithValue("@questId", state.QuestContextId);
            command.Parameters.AddWithValue("@status", (sbyte)state.Status);
            command.Parameters.AddWithValue("@dayKey", TodayKey.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Prepare();
            command.ExecuteNonQuery();
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            Logger.Warn(
                "TodayAssignment Persist skipped (table missing): owner={0} realStep={1}",
                ownerId, realStep);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TodayAssignment Persist failed owner={0} realStep={1}", ownerId, realStep);
        }
    }

    private void PersistResetsUsed(uint ownerId, uint used)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                REPLACE INTO character_today_reset_counts
                    (owner, day_key, resets_used)
                VALUES
                    (@owner, @dayKey, @used)
                """;
            command.Parameters.AddWithValue("@owner", ownerId);
            command.Parameters.AddWithValue("@dayKey", TodayKey.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@used", used);
            command.Prepare();
            command.ExecuteNonQuery();
        }
        catch (MySqlException ex) when (ex.Number is 1146 or 1054)
        {
            Logger.Warn(
                "TodayAssignment PersistResetsUsed skipped (table missing): owner={0}",
                ownerId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TodayAssignment PersistResetsUsed failed owner={0}", ownerId);
        }
    }

    private bool TryGetCharState(uint characterId, out Dictionary<uint, ActiveTodayAssignment> byStep)
        => _active.TryGetValue(characterId, out byStep);

    private bool TryGetActive(uint characterId, uint realStep, out ActiveTodayAssignment active)
    {
        active = null;
        if (!_active.TryGetValue(characterId, out var byStep))
            return false;
        return byStep.TryGetValue(realStep, out active);
    }

    private void SetActive(uint characterId, uint realStep, ActiveTodayAssignment active)
    {
        if (!_active.TryGetValue(characterId, out var byStep))
        {
            byStep = [];
            _active[characterId] = byStep;
        }

        byStep[realStep] = active;
    }

    private static void SendState(
        Character character,
        uint realStep,
        ActiveTodayAssignment state,
        bool init)
    {
        // First packet field is today_quest_steps.id (catalog key), not real_step.
        var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
        if (step == null)
        {
            Logger.Warn(
                "TodayAssignment SendState: no step for realStep={0} char={1}",
                realStep, character?.Name);
            return;
        }

        character.SendPacket(new SCTodayAssignmentChangedPacket(
            (int)step.Id,
            (int)state.GroupId,
            (int)state.QuestContextId,
            (sbyte)state.Status,
            init));
    }
}

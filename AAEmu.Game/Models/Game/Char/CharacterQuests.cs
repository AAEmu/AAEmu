using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Spheres;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterQuests(Character owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<uint> _removed = [];
    private readonly ConcurrentDictionary<uint, ObservedQuestDoodad> _observedQuestDoodads = [];

    private readonly record struct ObservedQuestDoodad(uint TemplateId, uint ZoneId);

    private Character Owner { get; set; } = owner;
    public Dictionary<uint, Quest> ActiveQuests { get; } = [];
    private Dictionary<ushort, CompletedQuest> CompletedQuests { get; } = [];
    private readonly List<(uint CinemaId, QuestComponentTemplate Component)> _cinemaEndEffects = [];

    public void BindPlayingCinema(uint cinemaId)
    {
        if (cinemaId != 0)
            Owner.CurrentlyPlayingCinemaId = cinemaId;
    }

    /// <summary>
    /// Permission / talk may already be playing this cinema before Progress
    /// initializes. Credit it now so complete + next accept happen during the film.
    /// </summary>
    public void BindAndCreditPlayingCinema(uint cinemaId)
    {
        if (!QuestCinemaBindRules.ShouldReplacePlaying(Owner.CurrentlyPlayingCinemaId, cinemaId))
            return;

        var alreadyPlaying = QuestCinemaBindRules.ShouldCreditOnEnterProgress(
            Owner.CurrentlyPlayingCinemaId, cinemaId);
        Owner.CurrentlyPlayingCinemaId = cinemaId;
        if (alreadyPlaying)
            Owner.Events.OnCinemaStarted(Owner, new OnCinemaStartedArgs { CinemaId = cinemaId });
    }

    public void EnqueueCinemaEndEffects(uint cinemaId, QuestComponentTemplate component)
    {
        if (cinemaId == 0 || component == null)
            return;
        _cinemaEndEffects.Add((cinemaId, component));
        Logger.Info(
            "Quest component buff deferred until cinema={0} component={1} buff={2}",
            cinemaId,
            component.Id,
            component.BuffId);
    }

    public IReadOnlyList<uint> DeferredCinemaIds()
    {
        if (_cinemaEndEffects.Count == 0)
            return [];
        var ids = new uint[_cinemaEndEffects.Count];
        for (var i = 0; i < _cinemaEndEffects.Count; i++)
            ids[i] = _cinemaEndEffects[i].CinemaId;
        return ids;
    }

    public uint ResolvePlayingCinemaId(uint reported) =>
        QuestCinemaBindRules.ResolveCompletedCinema(reported, DeferredCinemaIds());

    public void ApplyCinemaEndEffects(uint cinemaId)
    {
        if (_cinemaEndEffects.Count == 0)
            return;
        cinemaId = ResolvePlayingCinemaId(cinemaId);
        if (cinemaId == 0)
        {
            Logger.Warn(
                "Quest cinema-end skipped, playing id is 0 with {0} deferred buff(s)",
                _cinemaEndEffects.Count);
            return;
        }

        var applied = 0;
        for (var i = 0; i < _cinemaEndEffects.Count;)
        {
            var pending = _cinemaEndEffects[i];
            if (pending.CinemaId != cinemaId)
            {
                i++;
                continue;
            }
            applied++;

            _cinemaEndEffects.RemoveAt(i);
            Logger.Info(
                "Quest cinema-end component buff cinema={0} component={1} buff={2}",
                cinemaId,
                pending.Component.Id,
                pending.Component.BuffId);

            var questId = pending.Component.ParentQuestTemplate?.Id ?? 0;
            Quest quest = null;
            var active = questId != 0 && ActiveQuests.TryGetValue(questId, out quest);
            var completed = questId != 0 && HasQuestCompleted(questId);
            if (!QuestCinemaBindRules.ShouldApplyCinemaEndEffect(active, completed))
                continue;

            if (quest != null)
            {
                quest.UseSkillAndBuff(pending.Component);
            }
            else if (pending.Component.SkillId > 0 || pending.Component.BuffId > 0)
            {
                // Resolve the manager only when the component carries an effect.
                QuestComponentEffectRules.ApplySkillAndBuff(Owner, pending.Component, SkillManager.Instance);
            }
        }

        if (applied == 0)
        {
            Logger.Warn(
                "Quest cinema-end had no buff for cinema={0}, deferred={1}",
                cinemaId,
                _cinemaEndEffects.Count);
        }
    }

    /// <summary>
    /// Leave-world flush. Once the session is gone the client never reports the cinema
    /// end, and the quest step is already saved, so apply the pending entries now instead
    /// of dropping the quest's buff or teleport with this in-memory list.
    /// </summary>
    public void FlushPendingCinemaEndEffects()
    {
        if (_cinemaEndEffects.Count == 0)
            return;

        var cinemas = new List<uint>();
        foreach (var pending in _cinemaEndEffects)
        {
            if (!cinemas.Contains(pending.CinemaId))
                cinemas.Add(pending.CinemaId);
        }

        foreach (var cinemaId in cinemas)
            ApplyCinemaEndEffects(cinemaId);
    }

    /// <summary>
    /// Queues the cinema-end entries a previous session could not finish. The component id
    /// is the only durable name for the effect, so a component that no longer exists is
    /// dropped with a warning. Production resolves through the quest manager; tests inject
    /// their own resolver. The queued entries are applied by
    /// <see cref="FlushPendingCinemaEndEffects"/> after world entry or on leave — never
    /// during load, where the buff packet has no connection.
    /// </summary>
    public void RestorePendingCinemaEndEffects(
        IReadOnlyList<(uint QuestId, uint CinemaId, uint ComponentId)> rows,
        Func<uint, QuestComponentTemplate> componentResolver = null)
    {
        if (rows == null || rows.Count == 0)
            return;

        componentResolver ??= QuestManager.Instance.GetComponent;

        foreach (var row in rows)
        {
            var component = componentResolver(row.ComponentId);
            if (component == null)
            {
                Logger.Warn(
                    "Pending cinema-end component {0} for quest {1} no longer exists, dropped",
                    row.ComponentId,
                    row.QuestId);
                continue;
            }

            EnqueueCinemaEndEffects(row.CinemaId, component);
        }
    }

    public bool HasQuest(uint questId)
    {
        return ActiveQuests.ContainsKey(questId);
    }

    public bool HasQuestCompleted(uint questId)
    {
        var questBlockId = (ushort)(questId / 64);
        var questBlockIndex = (int)(questId % 64);
        return CompletedQuests.TryGetValue(questBlockId, out var questBlock) && questBlock.Body.Get(questBlockIndex);
    }

    /// <summary>
    /// Starts a given quest from specific defined quest starter
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="forcibly"></param>
    /// <param name="questAcceptorType"></param>
    /// <param name="acceptorId"></param>
    /// <param name="answerClient">
    /// True only when the character asked for this accept and has a window to close. Zone spheres,
    /// level-up chains, guild assignment probes and GM commands all reach this method without one, and
    /// a failure they cannot see must stay silent.
    /// </param>
    /// <returns></returns>
    public bool AddQuest(uint questId, bool forcibly = false, QuestAcceptorType questAcceptorType = QuestAcceptorType.Unknown, uint acceptorId = 0, bool answerClient = false)
    {
        // Sort-6 quests are shared guild state. Accepting one as a personal quest would execute its
        // item and guild reward acts once per character and duplicate the authoritative guild award.
        if (TodayQuestGameData.Instance.IsExpeditionPublicQuest(questId))
        {
            Logger.Warn("Rejected personal start of public guild assignment quest {0} for {1}",
                questId, Owner.Name);
            NotifyAcceptFailed(questId, QuestAcceptFailRules.PublicAssignmentBlocked, answerClient);
            return false;
        }

        if (ActiveQuests.ContainsKey(questId))
        {
            if (forcibly)
            {
                Logger.Info($"[GM] quest {questId}, added!");
                DropQuest(questId, true);
            }
            else
            {
                Logger.Info($"Duplicate quest {questId}, not added!");
                NotifyAcceptFailed(questId, QuestStatusFailed.AlreadyHave, answerClient);
                return false;
            }
        }

        var template = QuestManager.Instance.GetTemplate(questId);
        if (template == null)
        {
            Logger.Error($"Failed to start new Quest {questId}, invalid Id");
            NotifyAcceptFailed(questId, QuestStatusFailed.InvalidQuest, answerClient);
            return false;
        }

        // A Reward act the server cannot grant yet refuses the accept (QuestRewardSupportRules):
        // holding the quest at Reward would re-run its sibling grants on every re-evaluation, and
        // finishing it would hand the quest out without that reward.
        if (QuestRewardSupportRules.RefusesAccept(template, forcibly))
        {
            var unsupported = QuestRewardSupportRules.FirstUnsupported(template);
            LogAcceptRefused(answerClient,
                "User {0} ({1}) cannot accept quest {2}: its {3} reward needs {4}, which the server does not have",
                Owner.Name, Owner.Id, questId, unsupported.GetType().Name, unsupported.MissingSubsystem);
            NotifyAcceptFailed(questId, QuestAcceptFailRules.RequirementNotMet, answerClient);
            return false;
        }

        // Start's own level range is a second gate the context level cannot cover: quest 10930 has
        // min_level 0 and a 10..19 range act. RunCurrentStep's false is dropped below, so an
        // out-of-range accept would sit in the journal at Start instead of being refused.
        if (!forcibly && QuestAcceptLevelRangeRules.RefusesAccept(template, Owner.Level))
        {
            LogAcceptRefused(answerClient,
                "User {0} ({1}) does not meet the Start level range for quest {2}: level={3}",
                Owner.Name, Owner.Id, questId, Owner.Level);
            NotifyAcceptFailed(questId, QuestAcceptFailRules.LevelNotMet, answerClient);
            return false;
        }

        if (!forcibly && !template.MeetsContextRequirements(Owner))
        {
            LogAcceptRefused(answerClient,
                "User {0} ({1}) does not meet context requirements for quest {2}: level={3}, minLevel={4}, maxLevel={5}, race={6}, raceMask={7}",
                Owner.Name, Owner.Id, questId, Owner.Level, template.MinLevel, template.MaxLevel, Owner.Race,
                template.RaceMask);
            // The client names the level gate itself; race and the start unit_reqs share the generic row.
            NotifyAcceptFailed(
                questId,
                template.MeetsLevelRequirements(Owner)
                    ? QuestAcceptFailRules.RequirementNotMet
                    : QuestAcceptFailRules.LevelNotMet,
                answerClient);
            return false;
        }

        // Check if start step components are active
        var startComponentTemplate = template.GetComponents(QuestComponentKind.Start);
        foreach (var questComponentTemplate in startComponentTemplate)
        {
            if (!UnitRequirementsGameData.Instance.CanComponentRun(questComponentTemplate, Owner))
            {
                LogAcceptRefused(answerClient, $"User {Owner.Name} ({Owner.Id}) does not meet requirements to start new Quest {questId}, ComponentId {questComponentTemplate.Id}");
                if (!forcibly)
                {
                    NotifyAcceptFailed(questId, QuestAcceptFailRules.RequirementNotMet, answerClient);
                    return false;
                }
            }
        }

        if (HasQuestCompleted(questId))
        {
            if (forcibly)
            {
                Logger.Info($"[GM] quest {questId}, added!");
                DropQuest(questId, true);
            }
            else if (template.Repeatable == false)
            {
                Logger.Warn($"Quest {questId} already completed for {Owner.Name}, not added!");
                NotifyAcceptFailed(questId, QuestStatusFailed.AlreadyCompleted, answerClient);
                return false;
            }
        }

        // /quest add does not pass an NPC. Start's AcceptNpc act then stays
        // false, Progress never arms, and interactables refuse the quest.
        if (forcibly)
            QuestAcceptRules.FillUnknownAcceptor(template, ref questAcceptorType, ref acceptorId);

        // Create new Quest Object
        var quest = new Quest(template, Owner)
        {
            Id = QuestIdManager.Instance.GetNextId(),
            Status = QuestStatus.Invalid,
            Condition = QuestConditionObj.Progress,
            QuestAcceptorType = questAcceptorType,
            AcceptorId = acceptorId
        };

        // If there's still a timer running for this quest, remove it
        if (QuestManager.Instance.QuestTimeoutTask.Count != 0)
        {
            if (QuestManager.Instance.QuestTimeoutTask.TryGetValue(quest.Owner.Id, out var value))
            {
                value.Remove(questId);
            }
        }

        // Actually start the quest by setting step to Start and send the quest start packets
        var res = quest.StartQuest();
        if (!res)
        {
            // If it failed to start, drop the quest here
            DropQuest(questId, true);
            NotifyAcceptFailed(questId, QuestStatusFailed.InvalidQuestStatus, answerClient);
            return false;
        }

        // Add it to the Active Quests
        ActiveQuests.Add(quest.TemplateId, quest);
        quest.Owner.SendDebugMessage($"[Quest] {Owner.Name}, quest {questId} added.");

        // Execute the first Step
        _ = quest.RunCurrentStep(); // We don't need the return value here

        quest.QuestInitialized();
        return true;
    }

    /// <summary>
    /// Answers a refused accept. The client is told only when it asked: a zone sphere, a quest chain,
    /// a guild assignment probe or a GM command all reach these refusals with no window open, and an
    /// unsolicited error there is noise the character cannot act on.
    /// </summary>
    private void NotifyAcceptFailed(uint questId, QuestStatusFailed reason, bool answerClient)
    {
        if (!answerClient)
            return;

        Owner.SendPacket(new SCQuestContextFailedPacket(questId, reason));
    }

    /// <summary>
    /// Logs a refused accept at the level its origin deserves: see
    /// <see cref="QuestAcceptFailRules.RefusalLogLevel"/>.
    /// </summary>
    private static void LogAcceptRefused(bool answerClient, string message, params object[] args) =>
        Logger.Log(QuestAcceptFailRules.RefusalLogLevel(answerClient), message, args);

    /// <summary>
    /// Starts a Quest given by a NPC
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="npcObjId">ObjectId of the NPC</param>
    /// <param name="answerClient">True when the character's own accept is being served.</param>
    /// <returns></returns>
    public bool AddQuestFromNpc(uint questId, uint npcObjId, bool answerClient = false)
    {
        var npc = Owner.ParentWorld.GetNpc(npcObjId);
        if (npc == null)
        {
            Logger.Warn("AddQuestFromNpc: NPC objId {0} not found for quest {1}", npcObjId, questId);
            NotifyAcceptFailed(questId, QuestAcceptFailRules.MissingSource(QuestAcceptorType.Npc), answerClient);
            return false;
        }
        Owner.CurrentTarget = npc;
        return AddQuest(questId, false, QuestAcceptorType.Npc, npc.TemplateId, answerClient);
    }

    /// <summary>
    /// Starts a Quest given by a Doodad
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="doodadObjId">ObjectId of the Doodad</param>
    /// <param name="answerClient">True when the character's own accept is being served.</param>
    /// <returns></returns>
    public bool AddQuestFromDoodad(uint questId, uint doodadObjId, bool answerClient = false)
    {
        var doodad = Owner.ParentWorld.GetDoodad(doodadObjId);
        if (doodad != null)
        {
            var started = AddQuest(questId, false, QuestAcceptorType.Doodad, doodad.TemplateId, answerClient);
            if (started)
                doodad.RefreshQuestReactFor(Owner);
            return started;
        }

        if (!_observedQuestDoodads.TryGetValue(doodadObjId, out var observed) ||
            observed.ZoneId != Owner.Transform.ZoneId ||
            !DoodadManager.Instance.OffersQuest(observed.TemplateId, questId))
        {
            Logger.Warn("AddQuestFromDoodad: doodad objId {0} not found for quest {1}", doodadObjId, questId);
            NotifyAcceptFailed(questId, QuestAcceptFailRules.MissingSource(QuestAcceptorType.Doodad), answerClient);
            return false;
        }

        return AddQuest(questId, false, QuestAcceptorType.Doodad, observed.TemplateId, answerClient);
    }

    public bool ObserveQuestDoodad(uint doodadObjId, uint doodadTemplateId)
    {
        if (doodadObjId == 0 || DoodadManager.Instance.GetTemplate(doodadTemplateId) == null)
            return false;

        _observedQuestDoodads[doodadObjId] = new ObservedQuestDoodad(doodadTemplateId, Owner.Transform.ZoneId);
        return true;
    }

    /// <summary>
    /// QuestReact rows only live on the current phase. Re-apply the whole chain nearby
    /// after a step change so the body flips without a leave/re-enter. The chain is not
    /// filtered by <paramref name="questId"/>: rows are ordered and an earlier quest can
    /// still hold the phase.
    /// </summary>
    public void ApplyNearbyQuestReacts(uint questId)
    {
        if (Owner == null || questId == 0)
            return;

        foreach (var doodad in WorldManager.GetAround<Doodad>(Owner))
            doodad?.RefreshQuestReactFor(Owner);
    }

    /// <summary>
    /// Starts a Quest by entering a Sphere
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="sphereId"></param>
    /// <param name="answerClient">True when the character's own accept is being served.</param>
    /// <returns></returns>
    public bool AddQuestFromSphere(uint questId, uint sphereId, bool answerClient = false)
    {
        return AddQuest(questId, false, QuestAcceptorType.Sphere, sphereId, answerClient);
    }

    /// <summary>
    /// Starts a Quest from a given Item
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="itemTemplateId"></param>
    /// <returns></returns>
    public bool AddQuestFromItem(uint questId, uint itemTemplateId)
    {
        return AddQuest(questId, false, QuestAcceptorType.Item, itemTemplateId);
    }

    /// <summary>
    /// Starts a Quest from executing a Skill
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="skillTemplateId"></param>
    /// <returns></returns>
    public bool AddQuestFromSkill(uint questId, uint skillTemplateId)
    {
        return AddQuest(questId, false, QuestAcceptorType.Skill, skillTemplateId);
    }

    /// <summary>
    /// Starts a Quest from a Buff
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="buffTemplateId"></param>
    /// <returns></returns>
    public bool AddQuestFromBuff(uint questId, uint buffTemplateId)
    {
        return AddQuest(questId, false, QuestAcceptorType.Buff, buffTemplateId);
    }

    /// <summary>
    /// Removes a quest
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="update"></param>
    /// <param name="forcibly"></param>
    public void DropQuest(uint questId, bool update, bool forcibly = false)
    {
        if (!ActiveQuests.TryGetValue(questId, out var quest)) { return; }

        quest.SkipUpdatePackets(); // make sure no further "update packets" are send to the player
        quest.Cleanup();
        quest.Drop(update);
        quest.FinalizeQuestActs();
        if (QuestCinemaBindRules.ShouldClearCinemaEndOnDrop(HasQuestCompleted(questId), forcibly))
            ClearCinemaEndEffects(questId);
        ActiveQuests.Remove(questId);
        _removed.Add(questId);

        if (forcibly)
        {
            SetCompletedQuestFlag(questId, false);
        }

        quest.Owner.SendDebugMessage($"[Quest] for player: {Owner.Name}, quest: {questId} removed.");
        Logger.Warn($"[Quest] for player: {Owner.Name}, quest: {questId} removed.");

        QuestManager.Instance.RemoveQuestTimer(Owner.Id, questId);

        QuestIdManager.Instance.ReleaseId((uint)quest.Id);
    }

    private void ClearCinemaEndEffects(uint questId)
    {
        if (questId == 0)
            return;
        for (var i = _cinemaEndEffects.Count - 1; i >= 0; i--)
        {
            var pendingQuestId = _cinemaEndEffects[i].Component?.ParentQuestTemplate?.Id ?? 0;
            if (QuestCinemaBindRules.CinemaEndBelongsToQuest(pendingQuestId, questId))
                _cinemaEndEffects.RemoveAt(i);
        }
    }

    /// <summary>
    /// Helper function for /quest GM command
    /// </summary>
    /// <param name="questContextId"></param>
    /// <param name="step"></param>
    /// <param name="selectedReward"></param>
    /// <returns></returns>
    public bool SetStep(uint questContextId, uint step, int selectedReward = -1)
    {
        if (step > 8)
            return false;

        if (!ActiveQuests.TryGetValue(questContextId, out var quest))
            return false;

        if (selectedReward >= 0)
            quest.SelectedRewardIndex = selectedReward;
        quest.Step = (QuestComponentKind)step;
        return true;
    }

    /// <summary>
    /// Player manually tossed this quest item, checks if this action should remove the quest or not
    /// </summary>
    /// <param name="item"></param>
    public void OnQuestItemManuallyDestroyed(Item item)
    {
        // Check if the quest needs to be cancelled
        if (item.Template.LootQuestId <= 0)
            return;

        // Check all the quests
        var doDropQuest = false;
        foreach (var quest in ActiveQuests.Values.ToList())
        {
            // Go through the steps in reverse order starting from the currently active one
            // This is needed because it's possible for the same item to be used in multiple acts, but will only cancel
            // the quest if it's on a specific step in the quest progress
            // For example: "The Mad Scholar" ( 3544 ), where "Kyrios' Helm Fragment" ( 21500 ) would only cancel the
            // quest if it's happening on the quest supply part.
            // From what I think needs to happen is that the DropOnDestroy setting from the last used/active
            // is the only one that counts. If you encounter any setting, stop looking and evaluate that one.

            for (var step = quest.Step; step >= QuestComponentKind.Start; step--)
            {
                var currentComponents = quest.Template.GetComponents(step);
                foreach (var currentComponent in currentComponents)
                {
                    // Check if the item is related
                    foreach (var questActTemplate in currentComponent.ActTemplates)
                    {
                        var currentComponentAct = questActTemplate;

                        // QuestActConAcceptItem, QuestActObjItemGather, QuestActSupplyItem
                        if (currentComponentAct is IQuestActGenericItem iQuestActGenericItem && iQuestActGenericItem.ItemId == item.TemplateId)
                        {
                            if (iQuestActGenericItem.DropWhenDestroy)
                            {
                                doDropQuest = true;
                                break;
                            }
                            // it's a match, but we don't need to drop the quest, just exit
                            return;
                        }

                        // QuestActObjItemGroupGather
                        if (currentComponentAct is QuestActObjItemGroupGather questActObjItemGroupGather && QuestManager.Instance.CheckGroupItem(questActObjItemGroupGather.ItemGroupId, item.TemplateId))
                        {
                            if (questActObjItemGroupGather.DropWhenDestroy)
                            {
                                doDropQuest = true;
                                break;
                            }
                            // it's a match, but we don't need to drop the quest, just exit
                            return;
                        }

                        // QuestActObjItemGroupUse
                        if (currentComponentAct is QuestActObjItemGroupUse questActObjItemGroupUse && QuestManager.Instance.CheckGroupItem(questActObjItemGroupUse.ItemGroupId, item.TemplateId))
                        {
                            if (questActObjItemGroupUse.DropWhenDestroy)
                            {
                                doDropQuest = true;
                                break;
                            }
                            // it's a match, but we don't need to drop the quest, just exit
                            return;
                        }

                        // QuestActObjItemUse
                        if (currentComponentAct is QuestActObjItemUse questActObjItemUse && questActObjItemUse.ItemId == item.TemplateId)
                        {
                            if (questActObjItemUse.DropWhenDestroy)
                            {
                                doDropQuest = true;
                                break;
                            }
                            // it's a match, but we don't need to drop the quest, just exit
                            return;
                        }

                        if (doDropQuest)
                            break;
                    }

                    if (doDropQuest)
                        break;
                }
            }
            if (doDropQuest)
                break;
        }

        if (doDropQuest)
            Owner.Quests.DropQuest(item.Template.LootQuestId, true);
    }

    /// <summary>
    /// Sets given quest as (not) completed
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="isCompleted"></param>
    /// <returns>Returns the CompletedQuest block that was changed</returns>
    public CompletedQuest SetCompletedQuestFlag(uint questId, bool isCompleted)
    {
        // Calculate block and index
        var completedQuestBlockId = (ushort)(questId / 64);
        var completedQuestBlockIndex = (ushort)(questId % 64);
        // Grab or create block
        if (!CompletedQuests.TryGetValue(completedQuestBlockId, out var completedBlock))
        {
            completedBlock = new CompletedQuest(completedQuestBlockId);
            CompletedQuests.Add(completedQuestBlockId, completedBlock);
        }
        // Set quest flag to (not) completed
        completedBlock.Body.Set(completedQuestBlockIndex, isCompleted);
        if (isCompleted)
        {
            Owner.Events?.OnQuestComplete(Owner, new OnQuestCompleteArgs
            {
                QuestId = questId
            });
        }

        return completedBlock;
    }

    /// <summary>
    /// Checks if a given quest is marked as completed
    /// </summary>
    /// <param name="questId"></param>
    /// <returns></returns>
    public bool IsQuestComplete(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        if (!CompletedQuests.TryGetValue(completeId, out var completedQuest))
            return false;
        return completedQuest.Body[(int)(questId % 64)];
    }

    /// <summary>
    /// Sends the list of all active quests for the player (20 / packet)
    /// </summary>
    public void Send()
    {
        const int MaxEntriesPerPacket = 20;
        var quests = ActiveQuests.Values.ToArray();
        if (quests.Length <= MaxEntriesPerPacket)
        {
            Owner.SendPacket(new SCQuestsPacket(quests));
            return;
        }

        for (var i = 0; i < quests.Length; i += MaxEntriesPerPacket)
        {
            var size = quests.Length - i >= MaxEntriesPerPacket ? MaxEntriesPerPacket : quests.Length - i;
            var res = new Quest[size];
            Array.Copy(quests, i, res, 0, size);
            Owner.SendPacket(new SCQuestsPacket(res));
        }
    }

    /// <summary>
    /// Sends list of quest completed blocks (200 / packet)
    /// </summary>
    public void SendCompleted()
    {
        const int MaxEntriesPerPacket = 200;
        var completedQuests = CompletedQuests.Values.ToArray();
        if (completedQuests.Length <= MaxEntriesPerPacket)
        {
            Owner.SendPacket(new SCCompletedQuestsPacket(completedQuests));
            return;
        }

        for (var i = 0; i < completedQuests.Length; i += MaxEntriesPerPacket)
        {
            var size = completedQuests.Length - i >= MaxEntriesPerPacket ? MaxEntriesPerPacket : completedQuests.Length - i;
            var result = new CompletedQuest[size];
            Array.Copy(completedQuests, i, result, 0, size);
            Owner.SendPacket(new SCCompletedQuestsPacket(result));
        }
    }

    /// <summary>
    /// Push one dirty completed-quest bitset block after a turn-in so the client's
    /// unit_req kind-31 / IsCompleted readers see the finish without waiting for
    /// the next full <see cref="SendCompleted"/> (login / SendInitialState).
    /// </summary>
    public void SendCompletedBlock(CompletedQuest block)
    {
        if (block == null)
            return;
        Owner.SendPacket(new SCCompletedQuestsPacket([block]));
    }

    /// <summary>
    /// Sends active and completed lists. Char-select is not enough: in-world
    /// start/complete checks read this after the local player exists.
    /// </summary>
    public void SendInitialState()
    {
        Send();
        SendCompleted();
    }

    /// <summary>
    /// Clears completed_quests bits for every finished quest whose detail is in
    /// <paramref name="questDetail"/>. Active (in-progress) quests are left alone so mid-run
    /// work is not cancelled by the calendar edge.
    /// </summary>
    private void ResetQuests(QuestDetail[] questDetail, bool sendIfChanged = true)
    {
        if (questDetail == null || questDetail.Length == 0)
            return;

        var match = new HashSet<QuestDetail>(questDetail);
        var cleared = new List<uint>();

        foreach (var (completeBlockId, completeBlock) in CompletedQuests)
        {
            for (var blockIndex = 0; blockIndex < 64; blockIndex++)
            {
                if (!completeBlock.Body[blockIndex])
                    continue;

                var questId = (uint)(completeBlockId * 64) + (uint)blockIndex;
                var q = QuestManager.Instance.GetTemplate(questId);
                if (q == null)
                    continue;
                // Still active — do not touch.
                if (HasQuest(questId))
                    continue;
                if (!match.Contains(q.DetailId))
                    continue;

                completeBlock.Body.Set(blockIndex, false);
                cleared.Add(questId);
                Logger.Info("QuestReset by {0}, reset {1} detail={2}", Owner.Name, questId, q.DetailId);
            }
        }

        if (!sendIfChanged || cleared.Count == 0)
            return;

        // Prefer bulk 0x192 (≤255 per packet); remainder as singles if ever needed.
        var offset = 0;
        while (offset < cleared.Count)
        {
            var take = Math.Min(255, cleared.Count - offset);
            if (take == 1)
                Owner.SendPacket(new SCQuestContextResetPacket(cleared[offset]));
            else
                Owner.SendPacket(new SCQuestContextResetBulkPacket(cleared.GetRange(offset, take)));
            offset += take;
        }
    }

    /// <summary>
    /// Loads the list of completed and active quests from the MySQL DB for this player 
    /// </summary>
    /// <param name="connection"></param>
    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM completed_quests WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var quest = new CompletedQuest
                    {
                        Id = reader.GetUInt16("id"),
                        Body = new BitArray((byte[])reader.GetValue("data"))
                    };
                    CompletedQuests.Add(quest.Id, quest);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM quests WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var questId = reader.GetUInt32("id");
                    var templateId = reader.GetUInt32("template_id");

                    var template = QuestManager.Instance.GetTemplate(templateId);
                    if (template == null)
                    {
                        Logger.Error($"Quest {templateId} by {Owner.Name} does not exist");
                        continue;
                    }

                    var quest = new Quest(template, Owner)
                    {
                        Id = questId,
                        TemplateId = templateId,
                        Status = (QuestStatus)reader.GetByte("status")
                    };
                    var oldStatus = quest.Status;
                    quest.ReadData((byte[])reader.GetValue("data"));
                    quest.Status = oldStatus;
                    ActiveQuests.Add(quest.TemplateId, quest);
                    quest.QuestInitialized();
                    quest.RequestEvaluation();
                }
            }
        }

        // A pending cinema-end effect survives a dropped connection: the client never reports
        // the film ending and the step is already saved. The row is queued here and replayed
        // after world entry (the buff packet needs the live connection); it is cleared by the
        // character save that follows, so a failed apply cannot lose it.
        try
        {
            var pendingCinemaEnds = new List<(uint QuestId, uint CinemaId, uint ComponentId)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT `quest_id`,`cinema_id`,`component_id` FROM character_quest_cinema_end_effects WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        pendingCinemaEnds.Add((
                            reader.GetUInt32("quest_id"),
                            reader.GetUInt32("cinema_id"),
                            reader.GetUInt32("component_id")));
                    }
                }
            }

            RestorePendingCinemaEndEffects(pendingCinemaEnds);
        }
        catch (MySqlException ex)
        {
            // A missing table must not block login; the effect is lost exactly as before this change.
            Logger.Error(
                ex,
                "Pending cinema-end restore skipped for {0} — is the character_quest_cinema_end_effects update applied?",
                Owner.Name);
        }
    }

    /// <summary>
    /// Saves list of active and completed quests to MySQL DB
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removed.Count > 0)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                var ids = string.Join(",", _removed);
                command.CommandText = $"DELETE FROM quests WHERE owner = @owner AND template_id IN({ids})";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            _removed.Clear();
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO completed_quests(`id`,`data`,`owner`) VALUES(@id,@data,@owner)";
            foreach (var quest in CompletedQuests.Values)
            {
                command.Parameters.AddWithValue("@id", quest.Id);
                var body = new byte[8];
                quest.Body.CopyTo(body, 0);
                command.Parameters.AddWithValue("@data", body);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();

                command.Parameters.Clear();
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO quests(`id`,`template_id`,`data`,`status`,`owner`) VALUES(@id,@template_id,@data,@status,@owner)";

            foreach (var quest in ActiveQuests.Values)
            {
                command.Parameters.AddWithValue("@id", quest.Id);
                command.Parameters.AddWithValue("@template_id", quest.TemplateId);
                command.Parameters.AddWithValue("@data", quest.WriteData());
                command.Parameters.AddWithValue("@status", (byte)quest.Status);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();

                command.Parameters.Clear();
            }
        }

        // A cinema-end effect the film has not delivered yet belongs to the quest, so it is
        // saved with it. An abrupt disconnect saves the character without leaving the world,
        // and that path must not lose the pending buff or teleport.
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "DELETE FROM character_quest_cinema_end_effects WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }

            if (_cinemaEndEffects.Count > 0)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "INSERT INTO character_quest_cinema_end_effects(`owner`,`quest_id`,`cinema_id`,`component_id`) " +
                    "VALUES(@owner,@quest_id,@cinema_id,@component_id)";

                foreach (var pending in _cinemaEndEffects)
                {
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.Parameters.AddWithValue("@quest_id", pending.Component.ParentQuestTemplate?.Id ?? 0);
                    command.Parameters.AddWithValue("@cinema_id", pending.CinemaId);
                    command.Parameters.AddWithValue("@component_id", pending.Component.Id);
                    command.ExecuteNonQuery();

                    command.Parameters.Clear();
                }
            }
        }
        catch (MySqlException ex)
        {
            // The character save must still succeed when the update has not been applied yet.
            Logger.Error(
                ex,
                "Pending cinema-end save skipped for {0} — is the character_quest_cinema_end_effects update applied?",
                Owner.Name);
        }
    }

    /// <summary>
    /// Offline catch-up for calendar quests using leave_time as last-known wall-clock presence.
    /// Daily: 00:00 UTC; weekly: Monday 00:00 UTC. Matches online cron tasks even after World restarts.
    /// </summary>
    public void CheckDailyResetAtLogin()
    {
        CheckCalendarResetsAtLogin();
    }

    /// <summary>Applies any missed daily/weekly completion clears at login (no SC spam — client reloads list).</summary>
    public void CheckCalendarResetsAtLogin()
    {
        var leaveUtc = ServerCalendar.AsUtc(Owner.LeaveTime);

        if (leaveUtc.Date < ServerCalendar.TodayUtc)
            ResetDailyQuests(false);

        var leaveWeek = ServerCalendar.WeekStartMondayContaining(leaveUtc);
        if (leaveWeek < ServerCalendar.WeekStartMondayUtc)
            ResetWeeklyQuests(false);
    }

    /// <summary>Clears completed daily-detail quests.</summary>
    public void ResetDailyQuests(bool sendPacketsIfChanged)
    {
        ResetQuests(QuestCalendarResetSet.Daily, sendPacketsIfChanged);
    }

    /// <summary>Clears completed weekly-detail quests (detail_id = weekly).</summary>
    public void ResetWeeklyQuests(bool sendPacketsIfChanged)
    {
        ResetQuests(QuestCalendarResetSet.Weekly, sendPacketsIfChanged);
    }

    public void TryCompleteQuestAsLetItDone(uint questId, int selectedReward)
    {
        if (!ActiveQuests.TryGetValue(questId, out var quest))
            return; // Quest not active

        if (quest.Template.LetItDone == false)
            return; // Quest doesn't have early complete function

        if (quest.GetQuestObjectiveStatus() < QuestObjectiveStatus.CanEarlyComplete)
            return; // Quest not ready to turn in yet

        // Go to reward step
        quest.SelectedRewardIndex = selectedReward;
        quest.Step = QuestComponentKind.Reward;
    }

    /// <summary>
    /// Needed to fix the daily flowerpot quests
    /// </summary>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public List<QuestAct> GetActiveActsWithUseItem(ulong itemId)
    {
        var res = new List<QuestAct>();
        foreach (var (_, activeQuest) in ActiveQuests)
        {
            foreach (var component in activeQuest.CurrentStep.Components.Values)
            {
                foreach (var act in component.Acts)
                {
                    if (act.Template is QuestActObjItemUse questActObjItemUse)
                    {
                        if (questActObjItemUse.ItemId == itemId)
                            res.Add(act);
                    }
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Zone reported player entered a quest_area / district (ZWEnterArea).
    /// Wire areaId is Cry groupId (16=quest_area, 22=district), not spheres.id.
    /// On quest-ish groups: reconcile quest_area_sphere.g (stype→spheres.id) by player
    /// position, and re-fire active SphereQuestManager triggers (interim).
    /// </summary>
    public void OnZoneAreaEnter(uint areaId)
    {
        Logger.Debug("OnZoneAreaEnter {0} area={1} activeQuests={2}", Owner.Name, areaId, ActiveQuests.Count);

        if (areaId is 16 or 19 or 20 or 21)
        {
            ReconcileQuestAreaSpheres();
            TryRefireSphereTriggersAtPlayer();
        }

        foreach (var quest in ActiveQuests.Values)
        {
            try
            {
                quest.OnZoneAreaEnter(areaId);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Quest {0} OnZoneAreaEnter failed", quest.TemplateId);
            }
        }
    }

    /// <summary>Zone reported player left a quest_area / district (ZWLeaveArea).</summary>
    public void OnZoneAreaLeave(uint areaId)
    {
        Logger.Debug("OnZoneAreaLeave {0} area={1}", Owner.Name, areaId);

        if (areaId is 16 or 19 or 20 or 21)
        {
            ReconcileQuestAreaSpheres();
            TryRefireSphereExitsAtPlayer();
        }

        foreach (var quest in ActiveQuests.Values)
        {
            try
            {
                quest.OnZoneAreaLeave(areaId);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Quest {0} OnZoneAreaLeave failed", quest.TemplateId);
            }
        }
    }

    /// <summary>spheres.id currently inside via quest_area_sphere.g (Zone group-16 path).</summary>
    private readonly HashSet<uint> _insideQuestAreaSphereIds = [];

    /// <summary>
    /// Diff player position against loaded quest_area_sphere.g; fire enter/exit sphere acts
    /// and sphere-accept quests. Zone wire only carries groupId, so World owns sphere id map.
    /// Also used on a movement tick so SphereBuff areas (Ezi dock / slave customize) apply
    /// without waiting for another ZWEnterArea edge.
    /// </summary>
    public void ReconcileQuestAreaSpheres()
    {
        var world = Owner.ParentWorld;
        var sqm = world?.SphereQuestManager;
        if (sqm == null)
            return;

        var zoneId = Owner.Transform.ZoneId;
        var pos = Owner.Transform.World.Position;
        var nowInside = sqm.GetContainingQuestAreaSpheres(zoneId, pos);
        var nowIds = new HashSet<uint>();
        foreach (var geo in nowInside)
        {
            if (geo.SphereId == 0)
                continue;
            if (!CanTriggerQuestAreaSphere(geo.SphereId))
                continue;
            nowIds.Add(geo.SphereId);
            if (_insideQuestAreaSphereIds.Contains(geo.SphereId))
            {
                // Still inside: re-push SphereBuffs if a skill/impulse stripped them from the hull
                // while the character (and Ezi VFX) stayed in the area. Enter-only apply left
                // ships with no Moored heal until leave/re-enter.
                try
                {
                    EnsureSphereBuffWhileInside(geo.SphereId);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "QuestAreaSphere ensure-buff failed sphere={0} for {1}", geo.SphereId, Owner.Name);
                }
                continue;
            }
            try
            {
                ProcessQuestAreaSphereEnter(geo, pos);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "QuestAreaSphere enter failed sphere={0} for {1}", geo.SphereId, Owner.Name);
            }
        }

        foreach (var leftId in _insideQuestAreaSphereIds)
        {
            if (nowIds.Contains(leftId))
                continue;
            try
            {
                ProcessQuestAreaSphereExit(leftId, pos);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "QuestAreaSphere exit failed sphere={0} for {1}", leftId, Owner.Name);
            }
        }

        _insideQuestAreaSphereIds.Clear();
        foreach (var id in nowIds)
            _insideQuestAreaSphereIds.Add(id);
    }

    /// <summary>
    /// While still inside a SphereBuff volume, ensure char + slave_applicable mounts still have the buff.
    /// </summary>
    private bool CanTriggerQuestAreaSphere(uint sphereId)
    {
        var db = SphereGameData.Instance.GetSphere(sphereId);
        if (db == null)
            return true;
        return UnitRequirementsGameData.Instance.CanTriggerSphere(db, Owner);
    }

    private void EnsureSphereBuffWhileInside(uint sphereId)
    {
        var db = SphereGameData.Instance.GetSphere(sphereId);
        if (db?.SphereDetailType != "SphereBuff")
            return;
        if (!UnitRequirementsGameData.Instance.CanTriggerSphere(db, Owner))
            return;
        ApplySphereBuff(db.SphereDetailId, enter: true);
    }

    private void ProcessQuestAreaSphereEnter(SphereQuest geo, System.Numerics.Vector3 pos)
    {
        var sphereId = geo.SphereId;
        var db = SphereGameData.Instance.GetSphere(sphereId);
        if (db != null &&
            !UnitRequirementsGameData.Instance.CanTriggerSphere(db, Owner))
            return;

        Logger.Info("QuestAreaSphere ENTER char={0} sphere={1} zone={2}", Owner.Name, sphereId, geo.ZoneId);

        // SphereBuff: 13817 Moored (Docked, HealthRegen+200) and/or 13816 Ezi (collision/speed).
        if (db?.SphereDetailType == "SphereBuff")
            ApplySphereBuff(db.SphereDetailId, enter: true);

        // SphereAcceptQuest detail → auto-add listed quests
        if (db?.SphereDetailType == "SphereAcceptQuest")
        {
            foreach (var questId in SphereGameData.Instance.GetAcceptQuestIdsForSphereDetail(db.SphereDetailId))
            {
                if (!HasQuest(questId) && !HasQuestCompleted(questId))
                    AddQuestFromSphere(questId, sphereId);
            }
        }

        SphereGameData.Instance.TryResolveSphereQuestLink(sphereId, out var questIdLink, out var componentId);

        // SphereQuest detail: AcceptForce / AcceptConditional
        if (db?.SphereDetailType == "SphereQuest")
        {
            var detail = SphereGameData.Instance.GetSphereQuestDetail(db.SphereDetailId);
            if (detail != null)
            {
                questIdLink = detail.QuestId != 0 ? detail.QuestId : questIdLink;
                if (detail.QuestTriggerId is QuestTrigger.AcceptForce or QuestTrigger.AcceptConditional)
                {
                    if (!HasQuest(detail.QuestId) && !HasQuestCompleted(detail.QuestId))
                        AddQuestFromSphere(detail.QuestId, sphereId);
                }
            }
        }

        // QuestActConAcceptSphere keyed by this spheres.id
        if (questIdLink != 0 && componentId != 0)
        {
            var acts = QuestManager.Instance.GetActsInComponent(componentId);
            foreach (var act in acts)
            {
                if (act is QuestActConAcceptSphere accept && accept.SphereId == sphereId &&
                    !HasQuest(questIdLink) && !HasQuestCompleted(questIdLink))
                {
                    AddQuestFromSphere(questIdLink, sphereId);
                }
            }
        }

        // Prefer quest_sign_sphere geometry for the same quest (has ComponentId for ObjSphere).
        var fired = false;
        if (questIdLink != 0)
        {
            foreach (var sign in SphereQuestManager.GetSpheresForQuest(questIdLink))
            {
                if (!sign.Contains(pos))
                    continue;
                QuestManager.Instance.DoOnEnterSphereEvents(Owner, sign, pos);
                fired = true;
            }
        }

        if (!fired)
        {
            var eventSphere = new SphereQuest
            {
                WorldId = geo.WorldId,
                ZoneId = geo.ZoneId,
                SphereId = sphereId,
                QuestId = questIdLink,
                ComponentId = componentId,
                Xyz = geo.Xyz,
                Radius = geo.Radius
            };
            QuestManager.Instance.DoOnEnterSphereEvents(Owner, eventSphere, pos);
        }
    }

    private void ProcessQuestAreaSphereExit(uint sphereId, System.Numerics.Vector3 pos)
    {
        Logger.Info("QuestAreaSphere LEAVE char={0} sphere={1}", Owner.Name, sphereId);

        SphereGameData.Instance.TryResolveSphereQuestLink(sphereId, out var questIdLink, out var componentId);
        var db = SphereGameData.Instance.GetSphere(sphereId);
        if (db?.SphereDetailType == "SphereBuff")
            ApplySphereBuff(db.SphereDetailId, enter: false);

        if (db?.SphereDetailType == "SphereQuest")
        {
            var detail = SphereGameData.Instance.GetSphereQuestDetail(db.SphereDetailId);
            if (detail != null && detail.QuestId != 0)
                questIdLink = detail.QuestId;
        }

        var fired = false;
        if (questIdLink != 0)
        {
            foreach (var sign in SphereQuestManager.GetSpheresForQuest(questIdLink))
            {
                QuestManager.Instance.DoOnExitSphereEvents(Owner, sign, pos);
                fired = true;
            }
        }

        if (!fired)
        {
            var eventSphere = new SphereQuest
            {
                SphereId = sphereId,
                QuestId = questIdLink,
                ComponentId = componentId,
                ZoneId = Owner.Transform.ZoneId
            };
            QuestManager.Instance.DoOnExitSphereEvents(Owner, eventSphere, pos);
        }
    }

    private void ApplySphereBuff(uint sphereBuffDetailId, bool enter)
    {
        var detail = SphereGameData.Instance.GetSphereBuff(sphereBuffDetailId);
        if (detail == null)
            return;

        if (enter)
        {
            if (detail.BuffId == 0)
                return;

            var buffTemplate = SkillManager.Instance.GetBuffTemplate(detail.BuffId);
            var slaveApplicable = buffTemplate?.SlaveApplicable == true;

            if (SphereBuffTargets.ApplyToCharacter(slaveApplicable) &&
                !Owner.Buffs.CheckBuff(detail.BuffId))
            {
                Owner.Buffs.AddBuff(detail.BuffId, Owner);
                Logger.Info("SphereBuff APPLY char={0} buff={1} detail={2}", Owner.Name, detail.BuffId, sphereBuffDetailId);
            }

            // The helper gates hulls on slave_applicable itself; and_pet is independent of that.
            if (SphereBuffTargets.ApplyToOwnedMounts(slaveApplicable, detail.AndPet))
                ApplySphereBuffToOwnedMounts(detail.BuffId, detail.AndPet, add: true, sphereBuffDetailId);
            return;
        }

        var removeId = detail.RemoveOnLeaveBuffId != 0 ? detail.RemoveOnLeaveBuffId : detail.BuffId;
        if (removeId == 0)
            return;

        // Always strip leftover character copies (older sessions applied hull buffs to the PC).
        if (Owner.Buffs.CheckBuff(removeId))
        {
            Owner.Buffs.RemoveBuff(removeId);
            Logger.Info("SphereBuff REMOVE char={0} buff={1} detail={2}", Owner.Name, removeId, sphereBuffDetailId);
        }

        ApplySphereBuffToOwnedMounts(removeId, detail.AndPet, add: false, sphereBuffDetailId);
    }

    /// <summary>
    /// Re-push currently-active SphereBuffs onto owned mounts (e.g. after summoning a ship while
    /// already standing in the Two Crowns dock sphere — char already has Moored, so enter won't fire).
    /// </summary>
    public void SyncSphereBuffsToOwnedMounts()
    {
        foreach (var sphereId in _insideQuestAreaSphereIds)
        {
            var db = SphereGameData.Instance.GetSphere(sphereId);
            if (db?.SphereDetailType != "SphereBuff")
                continue;
            if (!UnitRequirementsGameData.Instance.CanTriggerSphere(db, Owner))
                continue;
            var detail = SphereGameData.Instance.GetSphereBuff(db.SphereDetailId);
            if (detail == null || detail.BuffId == 0)
                continue;
            ApplySphereBuffToOwnedMounts(detail.BuffId, detail.AndPet, add: true, db.SphereDetailId);
        }
    }

    /// <summary>
    /// Push or clear a sphere buff on the owner's active slaves (and mates when <paramref name="andPet"/>).
    /// Only buffs flagged <c>slave_applicable</c> are mirrored onto hulls.
    /// </summary>
    private void ApplySphereBuffToOwnedMounts(uint buffId, bool andPet, bool add, uint sphereBuffDetailId)
    {
        var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
        if (buffTemplate == null)
            return;

        var world = Owner.ParentWorld;
        if (world == null)
            return;

        if (buffTemplate.SlaveApplicable)
        {
            foreach (var slave in world.GetAllSlaves())
            {
                if (slave?.Summoner?.ObjId != Owner.ObjId && slave?.OwnerObjId != Owner.ObjId)
                    continue;

                if (add)
                {
                    if (!slave.Buffs.CheckBuff(buffId))
                    {
                        var oldMaxHp = slave.MaxHp;
                        var oldHp = slave.Hp;
                        slave.Buffs.AddBuff(buffId, Owner);
                        slave.Hp = SlaveHealthCapRules.AfterMaxHpChanged(oldHp, oldMaxHp, slave.MaxHp);
                        if (slave.Hp != oldHp)
                        {
                            slave.BroadcastPacket(new SCUnitPointsPacket(slave.ObjId, slave.Hp, slave.Mp), false);
                            slave.ParentWorld?.SlaveManager?.UpdateSlaveRepairPoints(slave);
                        }

                        Logger.Info("SphereBuff APPLY slave={0} buff={1} detail={2}", slave.Name, buffId, sphereBuffDetailId);
                    }
                }
                else if (slave.Buffs.CheckBuff(buffId))
                {
                    slave.Buffs.RemoveBuff(buffId);
                    Logger.Info("SphereBuff REMOVE slave={0} buff={1} detail={2}", slave.Name, buffId, sphereBuffDetailId);
                }
            }
        }

        if (!andPet)
            return;

        foreach (var mate in world.MateManager.GetActiveMates(Owner.Id) ?? [])
        {
            if (mate == null)
                continue;

            if (add)
            {
                if (mate.Buffs.CheckBuff(buffId))
                    continue;
                mate.Buffs.AddBuff(buffId, Owner);
                Logger.Info("SphereBuff APPLY mate={0} buff={1} detail={2}", mate.Name, buffId, sphereBuffDetailId);
            }
            else if (mate.Buffs.CheckBuff(buffId))
            {
                mate.Buffs.RemoveBuff(buffId);
                Logger.Info("SphereBuff REMOVE mate={0} buff={1} detail={2}", mate.Name, buffId, sphereBuffDetailId);
            }
        }
    }

    private void TryRefireSphereTriggersAtPlayer()
    {
        var world = Owner.ParentWorld;
        var sqm = world?.SphereQuestManager;
        if (sqm == null)
            return;

        var pos = Owner.Transform.World.Position;
        foreach (var trigger in sqm.GetSphereQuestTriggers())
        {
            if (trigger.Owner?.Id != Owner.Id || trigger.Sphere == null)
                continue;
            if (!trigger.Sphere.Contains(pos))
                continue;
            try
            {
                QuestManager.Instance.DoOnEnterSphereEvents(Owner, trigger.Sphere, pos);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Zone-area sphere re-fire failed for {0}", Owner.Name);
            }
        }
    }

    private void TryRefireSphereExitsAtPlayer()
    {
        var world = Owner.ParentWorld;
        var sqm = world?.SphereQuestManager;
        if (sqm == null)
            return;

        var pos = Owner.Transform.World.Position;
        foreach (var trigger in sqm.GetSphereQuestTriggers())
        {
            if (trigger.Owner?.Id != Owner.Id || trigger.Sphere == null)
                continue;
            // On leave of quest_area, notify exit for spheres the player was tracking.
            try
            {
                QuestManager.Instance.DoOnExitSphereEvents(Owner, trigger.Sphere, pos);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Zone-area sphere exit re-fire failed for {0}", Owner.Name);
            }
        }
    }
}

using System.Collections;
using System.Data;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterQuests(Character owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<uint> _removed = [];

    private Character Owner { get; set; } = owner;
    public Dictionary<uint, Quest> ActiveQuests { get; } = [];
    private Dictionary<ushort, CompletedQuest> CompletedQuests { get; } = [];

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
    /// <returns></returns>
    public bool AddQuest(uint questId, bool forcibly = false, QuestAcceptorType questAcceptorType = QuestAcceptorType.Unknown, uint acceptorId = 0)
    {
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
                return false;
            }
        }

        var template = QuestManager.Instance.GetTemplate(questId);
        if (template == null)
        {
            Logger.Error($"Failed to start new Quest {questId}, invalid Id");
            return false;
        }

        // Check if start step components are active
        var startComponentTemplate = template.GetComponents(QuestComponentKind.Start);
        foreach (var questComponentTemplate in startComponentTemplate)
        {
            if (!UnitRequirementsGameData.Instance.CanComponentRun(questComponentTemplate, Owner))
            {
                Logger.Trace($"User {Owner.Name} ({Owner.Id}) does not meet requirements to start new Quest {questId}, ComponentId {questComponentTemplate.Id}");
                if (!forcibly)
                    return false;
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
                Owner.SendErrorMessage(ErrorMessageType.QuestDailyLimit);
                return false;
            }
        }

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
    /// Starts a Quest given by a NPC
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="npcObjId">ObjectId of the NPC</param>
    /// <returns></returns>
    public bool AddQuestFromNpc(uint questId, uint npcObjId)
    {
        var npc = Owner.ParentWorld.GetNpc(npcObjId);
        if (npc == null)
        {
            Logger.Warn("AddQuestFromNpc: NPC objId {0} not found for quest {1}", npcObjId, questId);
            return false;
        }
        Owner.CurrentTarget = npc;
        return AddQuest(questId, false, QuestAcceptorType.Npc, npc.TemplateId);
    }

    /// <summary>
    /// Starts a Quest given by a Doodad
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="doodadObjId">ObjectId of the Doodad</param>
    /// <returns></returns>
    public bool AddQuestFromDoodad(uint questId, uint doodadObjId)
    {
        var doodad = Owner.ParentWorld.GetDoodad(doodadObjId);
        if (doodad == null)
        {
            Logger.Warn("AddQuestFromDoodad: doodad objId {0} not found for quest {1}", doodadObjId, questId);
            return false;
        }
        return AddQuest(questId, false, QuestAcceptorType.Doodad, doodad.TemplateId);
    }

    /// <summary>
    /// Starts a Quest by entering a Sphere
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="sphereId"></param>
    /// <returns></returns>
    public bool AddQuestFromSphere(uint questId, uint sphereId)
    {
        return AddQuest(questId, false, QuestAcceptorType.Sphere, sphereId);
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
    /// Resets all quests of a given types (used by ResetDailyQuests)
    /// </summary>
    /// <param name="questDetail"></param>
    /// <param name="sendIfChanged"></param>
    private void ResetQuests(QuestDetail[] questDetail, bool sendIfChanged = true)
    {
        foreach (var (completeBlockId, completeBlock) in CompletedQuests)
        {
            for (var blockIndex = 0; blockIndex < 64; blockIndex++)
            {
                var questId = (uint)(completeBlockId * 64) + (uint)blockIndex;
                var q = QuestManager.Instance.GetTemplate(questId);
                // Skip unused Ids
                if (q == null)
                    continue;
                // Skip if quest still active
                if (HasQuest(questId))
                    continue;

                foreach (var qd in questDetail)
                {
                    if (q.DetailId == qd && completeBlock.Body[blockIndex])
                    {
                        completeBlock.Body.Set(blockIndex, false);
                        Logger.Info($"QuestReset by {Owner.Name}, reset {questId}");
                        if (sendIfChanged)
                        {
                            var body = new byte[8];
                            completeBlock.Body.CopyTo(body, 0);
                            Owner.SendPacket(new SCQuestContextResetPacket(questId));
                        }
                    }
                }
            }
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
    }

    /// <summary>
    /// Checks if the player needs to reset daily quests based on last leave time (for use during login only) 
    /// </summary>
    public void CheckDailyResetAtLogin()
    {
        // TODO: Put Server timezone offset in configuration file, currently using local machine midnight
        // var utcDelta = DateTime.Now - DateTime.UtcNow;
        // var isOld = (DateTime.Today + utcDelta - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        var isOld = DateTime.UtcNow.Date - Owner.LeaveTime.Date >= TimeSpan.FromDays(1);
        if (isOld)
            ResetDailyQuests(false);
    }

    /// <summary>
    /// Resets all daily quests
    /// </summary>
    /// <param name="sendPacketsIfChanged"></param>
    public void ResetDailyQuests(bool sendPacketsIfChanged)
    {
        ResetQuests(
            [
                QuestDetail.Daily, QuestDetail.DailyGroup, QuestDetail.DailyHunt,
                QuestDetail.DailyLivelihood
            ], sendPacketsIfChanged
        );
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
    private void EnsureSphereBuffWhileInside(uint sphereId)
    {
        var db = SphereGameData.Instance.GetSphere(sphereId);
        if (db?.SphereDetailType != "SphereBuff")
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

            if (!Owner.Buffs.CheckBuff(detail.BuffId))
            {
                Owner.Buffs.AddBuff(detail.BuffId, Owner);
                Logger.Info("SphereBuff APPLY char={0} buff={1} detail={2}", Owner.Name, detail.BuffId, sphereBuffDetailId);
            }

            // slave_applicable: Moored (13817) HealthRegen+200 and Ezi (13816) collision/speed mods
            // belong on the hull. Character-only application left ships with formula regen ~0.
            ApplySphereBuffToOwnedMounts(detail.BuffId, detail.AndPet, add: true, sphereBuffDetailId);
            return;
        }

        var removeId = detail.RemoveOnLeaveBuffId != 0 ? detail.RemoveOnLeaveBuffId : detail.BuffId;
        if (removeId == 0)
            return;

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
                        slave.Buffs.AddBuff(buffId, Owner);
                        Logger.Info("SphereBuff APPLY slave={0} buff={1} detail={2}", slave.Name, buffId, sphereBuffDetailId);

                        // Ezi (13816) raises MaxHp by 10%, so the hull now sits below its cap and
                        // Moored's HealthRegen (+200/tick) repairs it back up — Slave.RegenTick pushes
                        // the points as it climbs. Nothing to snap here.
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;

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

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterQuests
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<uint> _removed;

    private Character Owner { get; set; }
    public Dictionary<uint, Quest> ActiveQuests { get; }
    private Dictionary<ushort, CompletedQuest> CompletedQuests { get; }

    public CharacterQuests(Character owner)
    {
        Owner = owner;
        ActiveQuests = new Dictionary<uint, Quest>();
        CompletedQuests = new Dictionary<ushort, CompletedQuest>();
        _removed = new List<uint>();
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
                Logger.Debug($"User {Owner.Name} ({Owner.Id}) does not meet requirements to start new Quest {questId}, ComponentId {questComponentTemplate.Id}");
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
        var quest = new Quest(template, Owner);
        quest.Id = QuestIdManager.Instance.GetNextId();
        quest.Status = QuestStatus.Invalid;
        quest.Condition = QuestConditionObj.Progress;
        quest.QuestAcceptorType = questAcceptorType;
        quest.AcceptorId = acceptorId;

        // If there's still a timer running for this quest, remove it
        if (QuestManager.Instance.QuestTimeoutTask.Count != 0)
        {
            if (QuestManager.Instance.QuestTimeoutTask.ContainsKey(quest.Owner.Id) && QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].ContainsKey(questId))
            {
                QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].Remove(questId);
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
        quest.Owner.SendMessage($"[Quest] {Owner.Name}, quest {questId} added.");

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
        var npc = WorldManager.Instance.GetNpc(npcObjId);
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
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
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

        quest.Owner.SendMessage($"[Quest] for player: {Owner.Name}, quest: {questId} removed.");
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
            // For example: "The Mad Scholar" ( 3544 ), where "Kyrios's Helm Fragment" ( 21500 ) would only cancel the
            // quest if it's happening on the quest supply part.
            // From what I think needs to happen is that the DropOnDestroy setting from the last used/active
            // is the only one that counts. If you encounter any setting, stop looking and evaluate that one.

            for(var step = quest.Step; step >= QuestComponentKind.Start; step--)
            {
                var currentComponents = quest.Template.GetComponents(step);
                foreach (var currentComponent in currentComponents)
                {
                    // Check if the item is related
                    foreach (var questActTemplate in currentComponent.ActTemplates)
                    {
                        var currentComponentAct = questActTemplate;

                        // QuestActConAcceptItem, QuestActObjItemGather, QuestActSupplyItem
                        if ((currentComponentAct is IQuestActGenericItem iQuestActGenericItem) && (iQuestActGenericItem.ItemId == item.TemplateId))
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
                        if ((currentComponentAct is QuestActObjItemGroupGather questActObjItemGroupGather) && (QuestManager.Instance.CheckGroupItem(questActObjItemGroupGather.ItemGroupId, item.TemplateId)))
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
                        if ((currentComponentAct is QuestActObjItemGroupUse questActObjItemGroupUse) && (QuestManager.Instance.CheckGroupItem(questActObjItemGroupUse.ItemGroupId, item.TemplateId)))
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
                        if ((currentComponentAct is QuestActObjItemUse questActObjItemUse) && (questActObjItemUse.ItemId == item.TemplateId))
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
                    if ((q.DetailId == qd) && (completeBlock.Body[blockIndex]))
                    {
                        completeBlock.Body.Set(blockIndex, false);
                        Logger.Info($"QuestReset by {Owner.Name}, reset {questId}");
                        if (sendIfChanged)
                        {
                            var body = new byte[8];
                            completeBlock.Body.CopyTo(body, 0);
                            Owner.SendPacket(new SCQuestContextResetPacket(questId, body, completeBlockId));
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
                    var quest = new CompletedQuest();
                    quest.Id = reader.GetUInt16("id");
                    quest.Body = new BitArray((byte[])reader.GetValue("data"));
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

                    var quest = new Quest(template, Owner);
                    quest.Id = questId;
                    quest.TemplateId = templateId;
                    quest.Status = (QuestStatus)reader.GetByte("status");
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
        var isOld = (DateTime.UtcNow.Date - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
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
            new []
            {
                QuestDetail.Daily, QuestDetail.DailyGroup, QuestDetail.DailyHunt,
                QuestDetail.DailyLivelihood
            }, sendPacketsIfChanged
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
                            res.Add((QuestAct)act);
                    }
                }
            }
        }
        return res;
    }
}

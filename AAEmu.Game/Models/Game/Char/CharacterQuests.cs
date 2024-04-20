using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public partial class CharacterQuests
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

        // TODO: Check/validate if this is needed
        // Verify if the quest zone is "friendly" towards the player before they can accept it.
        var questZoneKeyCheck = template.ZoneId;
        if ((template.ZoneId == 1) && (template.CategoryId != 9)) // zoneKey 1 = Gweonid Forest, Category 9 = Gweonid Forest
        {
            // Probably a generic non-zone specific quest
            questZoneKeyCheck = 0;
        }
        if (questZoneKeyCheck > 0)
        {
            var zone = ZoneManager.Instance.GetZoneByKey(questZoneKeyCheck);
            var zoneFaction = FactionManager.Instance.GetFaction(zone.FactionId);
            var relation = zoneFaction.GetRelationState(Owner.Faction);
            if (relation == RelationState.Hostile)
            {
                // Quest not allowed in hostile zones?
                Logger.Warn($"AddQuest trying to add a quest from a hostile zone, Player: {Owner.Name} ({Owner.Id}) {Owner.Faction.Name} ({Owner.Faction.Id}) for quest {questId} with ZoneKey: {questZoneKeyCheck}, Faction: {zoneFaction.Name} ({zoneFaction.Id})");
                Owner.SendMessage($"[AAEmu] AddQuest trying to add a quest from a hostile zone, Player: {Owner.Name} ({Owner.Id}) {Owner.Faction.Name} ({Owner.Faction.Id}) for quest {questId} with ZoneKey: {questZoneKeyCheck}, Faction: {zoneFaction.Name} ({zoneFaction.Id}), please report this to the admins with a screenshot attached");
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
        var quest = new Quest(template);
        quest.Id = QuestIdManager.Instance.GetNextId();
        quest.Status = QuestStatus.Invalid;
        quest.Condition = QuestConditionObj.Progress;
        quest.Owner = Owner;
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

        // Create the step objects based on the Quest Templates
        quest.CreateQuestSteps();

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
    /// Complete - завершаем квест, получаем награду
    /// </summary>
    /// <param name="questId"></param>
    /// <param name="selected"></param>
    /// <param name="supply"></param>
    public void CompleteQuest(uint questId, int selected, bool supply = true)
    {
        if (!ActiveQuests.TryGetValue(questId, out var quest))
        {
            Logger.Warn($"[CharacterQuests] {Owner.Name} CompleteQuest, quest does not exist {questId}");
            return;
        }

        quest.QuestRewardItemsPool.Clear();
        quest.QuestRewardCoinsPool = 0;
        quest.QuestRewardExpPool = 0;
        var res = quest.Complete(selected);
        if (res != 0)
        {
            if (supply)
            {
                var levelBasedRewards = QuestManager.Instance.GetSupplies(quest.Template.Level);
                if (levelBasedRewards != null)
                {
                    if (quest.Template.LetItDone)
                    {
                        // Add (or reduce) extra for over-achieving (or early completing) of the quest if allowed
                        quest.QuestRewardRatio = quest.GetQuestObjectivePercent(); // ratio is used by DistributeRewards
                        quest.QuestRewardExpPool += levelBasedRewards.Exp;
                        quest.QuestRewardCoinsPool += levelBasedRewards.Copper;

                        if (quest.GetQuestObjectiveStatus() < QuestObjectiveStatus.ExtraProgress)
                        {
                            // посылаем пакет, так как он был пропущен в методе Update()
                            // send a packet because it was skipped in the Update() method
                            quest.Status = QuestStatus.Progress;
                            // пакет не нужен
                            //Owner.SendPacket(new SCQuestContextUpdatedPacket(quest, quest.ComponentId));
                            quest.Status = QuestStatus.Completed;
                        }
                    }
                    else
                    {
                        quest.QuestRewardExpPool += levelBasedRewards.Exp;
                        quest.QuestRewardCoinsPool += levelBasedRewards.Copper;
                    }
                }
            }
            quest.DistributeRewards();

            // Mark Quest as Completed
            var completedBlock = SetCompletedQuestFlag(quest.TemplateId, true);
            // copy body data for packet
            var body = new byte[8];
            completedBlock.Body.CopyTo(body, 0);
            
            // Remove quest from list
            DropQuest(questId, false);
            
            // Send packet to player
            Owner.SendPacket(new SCQuestContextCompletedPacket(quest.TemplateId, body, res));
        }
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
    /// <returns></returns>
    public bool SetStep(uint questContextId, uint step)
    {
        if (step > 8)
            return false;

        if (!ActiveQuests.TryGetValue(questContextId, out var quest))
            return false;

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
                    var quest = new Quest();
                    quest.Id = reader.GetUInt32("id");
                    quest.TemplateId = reader.GetUInt32("template_id");
                    quest.Template = QuestManager.Instance.GetTemplate(quest.TemplateId);
                    quest.Owner = Owner;

                    quest.Status = (QuestStatus)reader.GetByte("status");
                    var oldStatus = quest.Status;
                    quest.CreateQuestSteps();
                    quest.ReadData((byte[])reader.GetValue("data"));
                    quest.Status = oldStatus;
                    ActiveQuests.Add(quest.TemplateId, quest);
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
        var isOld = (DateTime.Today - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        if (isOld)
            ResetDailyQuests(false);
    }

    /// <summary>
    /// Resets all daily quests
    /// </summary>
    /// <param name="sendPacketsIfChanged"></param>
    public void ResetDailyQuests(bool sendPacketsIfChanged)
    {
        Owner.Quests.ResetQuests(
            new []
            {
                QuestDetail.Daily, QuestDetail.DailyGroup, QuestDetail.DailyHunt,
                QuestDetail.DailyLivelihood
            }, sendPacketsIfChanged
        );
    }
}

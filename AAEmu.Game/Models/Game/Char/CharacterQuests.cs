﻿using System;
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
            Logger.Warn($"Complete, quest does not exist {questId}");
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
                        // Добавим|убавим за перевыполнение|недовыполнение плана, если позволено квестом
                        // Add [reduce] for overfulfilling [underperformance] of the plan, if allowed by the quest
                        // TODO: Verify if the bonus only applies to the level-based XP/Gold, or if it also applies to the rewards parts in quest_act_supply_xxx
                        var completeRate = quest.GetQuestObjectivePercent();
                        quest.QuestRewardExpPool += (int)(levelBasedRewards.Exp * completeRate);
                        quest.QuestRewardCoinsPool += (int)(levelBasedRewards.Copper * completeRate);

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

            var completeId = (ushort)(quest.TemplateId / 64);
            if (!CompletedQuests.ContainsKey(completeId))
                CompletedQuests.Add(completeId, new CompletedQuest(completeId));
            var complete = CompletedQuests[completeId];
            complete.Body.Set((int)(quest.TemplateId % 64), true);
            var body = new byte[8];
            complete.Body.CopyTo(body, 0);
            DropQuest(questId, false);
            //OnQuestComplete(questId);
            Owner.SendPacket(new SCQuestContextCompletedPacket(quest.TemplateId, body, res));
        }
    }

    public void DropQuest(uint questId, bool update, bool forcibly = false)
    {
        if (!ActiveQuests.ContainsKey(questId)) { return; }

        var quest = ActiveQuests[questId];
        quest.Cleanup();
        quest.Drop(update);
        ActiveQuests.Remove(questId);
        _removed.Add(questId);

        if (forcibly)
        {
            ResetCompletedQuest(questId);
        }

        quest.Owner.SendMessage($"[Quest] for player: {Owner.Name}, quest: {questId} removed.");
        Logger.Warn($"[Quest] for player: {Owner.Name}, quest: {questId} removed.");

        QuestManager.Instance.RemoveQuestTimer(Owner.Id, questId);

        QuestIdManager.Instance.ReleaseId((uint)quest.Id);
    }

    public bool SetStep(uint questContextId, uint step)
    {
        if (step > 8)
            return false;

        if (!ActiveQuests.TryGetValue(questContextId, out var quest))
            return false;

        quest.Step = (QuestComponentKind)step;
        return true;
    }

    public void OnReportToNpc(uint objId, uint questId, int selected)
    {
        if (!ActiveQuests.TryGetValue(questId, out var quest))
            return;

        var npc = WorldManager.Instance.GetNpc(objId);
        if (npc == null)
            return;

        //if (npc.GetDistanceTo(Owner) > 8.0f)
        //    return;

        quest.OnReportToNpc(npc, selected);
    }

    public void OnReportToDoodad(uint objId, uint questId, int selected)
    {
        if (!ActiveQuests.ContainsKey(questId))
            return;

        var quest = ActiveQuests[questId];

        var doodad = WorldManager.Instance.GetDoodad(objId);
        if (doodad == null)
            return;

        // if (npc.GetDistanceTo(Owner) > 8.0f)
        //     return;

        quest.OnReportToDoodad(doodad);
    }

    public void OnTalkMade(uint npcObjId, uint questContextId, uint questComponentId, uint questActId)
    {
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return;

        if (npc.GetDistanceTo(Owner) > 8.0f)
            return;

        if (!ActiveQuests.TryGetValue(questContextId, out var quest))
            return;

        quest.OnTalkMade(npc);
    }

    public void OnKill(Npc npc)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnKill(npc);
    }

    public void OnAggro(Npc npc)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnAggro(npc);
    }

    public void OnItemGather(Item item, int count)
    {
        //if (!Quests.ContainsKey(item.Template.LootQuestId))
        //    return;
        //var quest = Quests[item.Template.LootQuestId];
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnItemGather(item, count);
    }

    /// <summary>
    /// Использование предмета в инвентаре (Use of an item from your inventory)
    /// </summary>
    /// <param name="item"></param>
    public void OnItemUse(Item item)
    {
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnItemUse(item);
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
                            // it's a match, but we don't need drop the quest, just exit
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
                            // it's a match, but we don't need drop the quest, just exit
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
                            // it's a match, but we don't need drop the quest, just exit
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
                            // it's a match, but we don't need drop the quest, just exit
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
    /// Взаимодействие с doodad, например ломаем шахту по квесту (Interaction with doodad, for example, breaking a mine on a quest)
    /// </summary>
    /// <param name="type"></param>
    /// <param name="target"></param>
    public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnInteraction(type, target);
    }

    public void OnExpressFire(uint emotionId, uint objId, uint obj2Id)
    {
        var npc = WorldManager.Instance.GetNpc(obj2Id);
        if (npc == null)
            return;

        //if (npc.GetDistanceTo(Owner) > 8.0f)
        //    return;

        foreach (var quest in ActiveQuests.Values)
            quest.OnExpressFire(npc, emotionId);
    }

    public void OnLevelUp()
    {
        // LeveUp is kind of special in that it's trigger is mainly a quest starter
        foreach (var quest in ActiveQuests.Values)
            quest.OnLevelUp();
    }

    public void OnQuestComplete(uint questId)
    {
        foreach (var quest in ActiveQuests.Values)
            quest.OnQuestComplete(questId);
    }

    public void OnEnterSphere(SphereQuest sphereQuest)
    {
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnEnterSphere(sphereQuest);
    }

    public void OnCraft(Craft craft)
    {
        foreach (var quest in ActiveQuests.Values.ToList())
            quest.OnCraft(craft);
    }

    public void AddCompletedQuest(CompletedQuest quest)
    {
        CompletedQuests.Add(quest.Id, quest);
    }

    public void ResetCompletedQuest(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        var quest = GetCompletedQuest(completeId);

        if (quest == null) { return; }

        quest.Body.Set((int)questId - completeId * 64, false);
        CompletedQuests[completeId] = quest;
    }

    public CompletedQuest GetCompletedQuest(ushort id)
    {
        return CompletedQuests.TryGetValue(id, out var quest) ? quest : null;
    }

    public bool IsQuestComplete(uint questId)
    {
        var completeId = (ushort)(questId / 64);
        if (!CompletedQuests.ContainsKey(completeId))
            return false;
        return CompletedQuests[completeId].Body[(int)(questId - completeId * 64)];
    }

    public void Send()
    {
        var quests = ActiveQuests.Values.ToArray();
        if (quests.Length <= 20)
        {
            Owner.SendPacket(new SCQuestsPacket(quests));
            return;
        }

        for (var i = 0; i < quests.Length; i += 20)
        {
            var size = quests.Length - i >= 20 ? 20 : quests.Length - i;
            var res = new Quest[size];
            Array.Copy(quests, i, res, 0, size);
            Owner.SendPacket(new SCQuestsPacket(res));
        }
    }

    public void SendCompleted()
    {
        var completedQuests = CompletedQuests.Values.ToArray();
        if (completedQuests.Length <= 200)
        {
            Owner.SendPacket(new SCCompletedQuestsPacket(completedQuests));
            return;
        }

        for (var i = 0; i < completedQuests.Length; i += 20)
        {
            var size = completedQuests.Length - i >= 200 ? 200 : completedQuests.Length - i;
            var result = new CompletedQuest[size];
            Array.Copy(completedQuests, i, result, 0, size);
            Owner.SendPacket(new SCCompletedQuestsPacket(result));
        }
    }

    public void ResetQuests(QuestDetail questDetail, bool sendIfChanged = true) => ResetQuests(new QuestDetail[] { questDetail }, sendIfChanged);

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
                    quest.Status = (QuestStatus)reader.GetByte("status");
                    quest.Owner = Owner;
                    quest.Template = QuestManager.Instance.GetTemplate(quest.TemplateId);
                    var oldStatus = quest.Status;
                    quest.CreateQuestSteps();
                    quest.ReadData((byte[])reader.GetValue("data"));
                    var oldStep = quest.Step;
                    quest.RecalcObjectives(false);
                    // quest.RecallEvents();
                    quest.Step = oldStep;
                    quest.Status = oldStatus;
                    ActiveQuests.Add(quest.TemplateId, quest);
                }
            }
        }
    }

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

    public void CheckDailyResetAtLogin()
    {
        // TODO: Put Server timezone offset in configuration file, currently using local machine midnight
        // var utcDelta = DateTime.Now - DateTime.UtcNow;
        // var isOld = (DateTime.Today + utcDelta - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        var isOld = (DateTime.Today - Owner.LeaveTime.Date) >= TimeSpan.FromDays(1);
        if (isOld)
            ResetDailyQuests(false);
    }

    public void ResetDailyQuests(bool sendPacketsIfChanged)
    {
        Owner.Quests.ResetQuests(
            new QuestDetail[]
            {
                QuestDetail.Daily, QuestDetail.DailyGroup, QuestDetail.DailyHunt,
                QuestDetail.DailyLivelihood
            }, true
        );
    }

    public void RecallEvents()
    {
        foreach (var quest in Owner.Quests.ActiveQuests.Values)
        {
            quest.RecallEvents();
        }
    }
}

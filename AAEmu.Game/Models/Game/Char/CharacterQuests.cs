﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterQuests
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private readonly List<uint> _removed;

        public Character Owner { get; set; }

        public Dictionary<uint, Quest> Quests { get; }
        public Dictionary<ushort, CompletedQuest> CompletedQuests { get; }

        public CharacterQuests(Character owner)
        {
            Owner = owner;
            Quests = new Dictionary<uint, Quest>();
            CompletedQuests = new Dictionary<ushort, CompletedQuest>();
            _removed = new List<uint>();
        }

        public bool HasQuest(uint questId)
        {
            return Quests.ContainsKey(questId);
        }

        public void Add(uint questId)
        {
            if (Quests.ContainsKey(questId))
            {
                _log.Warn("Duplicate quest {0}, not added!", questId);
                return;
            }

            var template = QuestManager.Instance.GetTemplate(questId);
            if (template == null)
                return;
            var quest = new Quest(template);
            quest.Id = QuestIdManager.Instance.GetNextId();
            quest.Status = QuestStatus.Progress;
            quest.Owner = Owner;
            Quests.Add(quest.TemplateId, quest);

            var res = quest.Start();
            if (!res)
                Drop(questId, true); // TODO может быть update = false?
            //else
            //    Owner.SendPacket(new SCQuestContextStartedPacket(quest, res));
        }

        /// <summary>
        /// Метод предназначен для вызова из скрита QuestCmd, команда /quest add <questId>
        /// </summary>
        /// <param name="questId"></param>
        public void AddStart(uint questId)
        {
            if (Quests.ContainsKey(questId))
            {
                _log.Warn("Duplicate quest {0}, added!", questId);
                Drop(questId, true);
            }

            var template = QuestManager.Instance.GetTemplate(questId);
            if (template == null)
                return;
            var quest = new Quest(template);
            quest.Id = QuestIdManager.Instance.GetNextId();
            quest.Status = QuestStatus.Progress;
            quest.Owner = Owner;
            Quests.Add(quest.TemplateId, quest);

            quest.StartFirstOnly();
        }

        public void Complete(uint questId, int selected, bool supply = true)
        {
            if (!Quests.ContainsKey(questId))
            {
                _log.Warn("Complete not exist quest {0}", questId);
                return;
            }

            var quest = Quests[questId];
            var res = quest.Complete(selected);
            if (res != 0)
            {
                if (supply)
                {
                    var exps = quest.GetCustomExp();
                    var amount = quest.GetCustomCopper();
                    var supplies = QuestManager.Instance.GetSupplies(quest.Template.Level);
                    if (supplies != null)
                    {
                        if (quest.Template.LetItDone)
                        {
                            // Добавим|убавим за перевыполнение|недовыполнение плана, если позволено квестом
                            if (exps == 0)
                                Owner.AddExp(supplies.Exp * quest.OverCompletionPercent / 100, true);
                            if (amount == 0)
                                amount = supplies.Copper * quest.OverCompletionPercent / 100;
                            Owner.Money += amount;

                            if (!quest.ExtraCompletion)
                            {
                                // посылаем пакет, так как он был пропущен в методе Update()
                                quest.Status = QuestStatus.Progress;
                                Owner.SendPacket(new SCQuestContextUpdatedPacket(quest, quest.ComponentId));
                                quest.Status = QuestStatus.Completed;
                            }
                        }
                        else
                        {
                            if (exps == 0)
                                Owner.AddExp(supplies.Exp, true);
                            if (amount == 0)
                                amount = supplies.Copper;
                            Owner.Money += amount;
                        }

                        Owner.SendPacket(
                            new SCItemTaskSuccessPacket(
                                ItemTaskType.QuestComplete,
                                new List<ItemTask>
                                {
                                    new MoneyChange(amount)
                                },
                                new List<ulong>())
                        );
                    }
                }
                var completeId = (ushort)(quest.TemplateId / 64);
                if (!CompletedQuests.ContainsKey(completeId))
                    CompletedQuests.Add(completeId, new CompletedQuest(completeId));
                var complete = CompletedQuests[completeId];
                complete.Body.Set((int)(quest.TemplateId - completeId * 64), true);
                var body = new byte[8];
                complete.Body.CopyTo(body, 0);
                Drop(questId, false);
                Owner.SendPacket(new SCQuestContextCompletedPacket(quest.TemplateId, body, res));
                //OnQuestComplete(questId);
            }
        }

        public void Drop(uint questId, bool update)
        {
            if (!Quests.ContainsKey(questId))
                return;
            var quest = Quests[questId];
            quest.Drop(update);
            Quests.Remove(questId);
            _removed.Add(questId);

            quest.Owner.SendMessage("[Quest] {0}, quest {1} removed.", Owner.Name, questId);
            _log.Warn("[Quest] {0}, quest {1} removed.", Owner.Name, questId);

            if (QuestManager.Instance.QuestTimeoutTask.ContainsKey(questId))
            {
                _ = QuestManager.Instance.QuestTimeoutTask[questId].Cancel();
                QuestManager.Instance.QuestTimeoutTask.Remove(questId);
            }

            QuestIdManager.Instance.ReleaseId((uint)quest.Id);
        }

        public bool SetStep(uint questContextId, uint step)
        {
            if (step > 8)
                return false;

            if (!Quests.ContainsKey(questContextId))
                return false;

            var quest = Quests[questContextId];
            quest.Step = (QuestComponentKind)step;
            return true;
        }

        public void OnReportToNpc(uint objId, uint questId, int selected)
        {
            if (!Quests.ContainsKey(questId))
                return;

            var quest = Quests[questId];

            var npc = WorldManager.Instance.GetNpc(objId);
            if (npc == null)
                return;

            //if (npc.GetDistanceTo(Owner) > 8.0f)
            //    return;

            quest.OnReportToNpc(npc, selected);
        }

        public void OnReportToDoodad(uint objId, uint questId, int selected)
        {
            if (!Quests.ContainsKey(questId))
                return;

            var quest = Quests[questId];

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

            if (!Quests.ContainsKey(questContextId))
                return;

            var quest = Quests[questContextId];

            quest.OnTalkMade(npc);
        }

        public void OnKill(Npc npc)
        {
            foreach (var quest in Quests.Values.ToList())
                quest.OnKill(npc);
        }

        public void OnItemGather(Item item, int count)
        {
            if (!Quests.ContainsKey(item.Template.LootQuestId))
                return;
            var quest = Quests[item.Template.LootQuestId];
            quest.OnItemGather(item, count);
        }

        public void OnItemUse(Item item)
        {
            foreach (var quest in Quests.Values.ToList())
                quest.OnItemUse(item);
        }

        public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
        {
            foreach (var quest in Quests.Values)
                quest.OnInteraction(type, target);
        }

        public void OnLevelUp()
        {
            foreach (var quest in Quests.Values)
                quest.OnLevelUp();
        }

        public void OnQuestComplete(uint questId)
        {
            foreach (var quest in Quests.Values)
                quest.OnQuestComplete(questId);
        }

        public void OnEnterSphere(SphereQuest sphereQuest)
        {
            foreach (var quest in Quests.Values.ToList())
                quest.OnEnterSphere(sphereQuest);
        }

        public void AddCompletedQuest(CompletedQuest quest)
        {
            CompletedQuests.Add(quest.Id, quest);
        }

        public CompletedQuest GetCompletedQuest(ushort id)
        {
            return CompletedQuests.ContainsKey(id) ? CompletedQuests[id] : null;
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
            var quests = Quests.Values.ToArray();
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
                        quest.ReadData((byte[])reader.GetValue("data"));
                        quest.Owner = Owner;
                        quest.Template = QuestManager.Instance.GetTemplate(quest.TemplateId);
                        quest.RecalcObjectives(false);
                        Quests.Add(quest.TemplateId, quest);
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
                    command.Prepare();
                    command.Parameters.AddWithValue("@owner", Owner.Id);
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

                foreach (var quest in Quests.Values)
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
    }
}

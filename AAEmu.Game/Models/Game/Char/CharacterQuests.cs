using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterQuests
    {
        private readonly List<uint> _removed;
        
        public Dictionary<uint, Quest> Quests { get; }
        public Dictionary<ushort, CompletedQuest> CompletedQuests { get; }

        public Character Owner { get; set; }

        public CharacterQuests(Character owner)
        {
            Owner = owner;
            Quests = new Dictionary<uint, Quest>();
            CompletedQuests = new Dictionary<ushort, CompletedQuest>();
            _removed = new List<uint>();
        }

        public void Add(uint questId)
        {
            var template = QuestManager.Instance.GetTemplate(questId);
            if (template == null)
                return;
            var quest = new Quest(template);
            quest.Id = Quests.Count + 1; // TODO временно, переделать
            quest.Status = QuestStatus.Progress;
            quest.Owner = Owner;
            var res = quest.Start();
            if (res != 0)
            {
                Owner.SendPacket(new SCQuestContextStartedPacket(quest, res));
                Quests.Add(quest.TemplateId, quest);
            }
        }

        public void Complete(uint questId, int selected)
        {
            var quest = Quests[questId];
            var res = quest.Complete(selected);
            if (res != 0)
            {
                var supplies = QuestManager.Instance.GetSupplies(quest.Template.Level);
                if (supplies != null)
                {
                    Owner.AddExp(supplies.Exp, true);
                    Owner.Money += supplies.Copper;
                    Owner.SendPacket(
                        new SCItemTaskSuccessPacket(
                            ItemTaskType.QuestComplete,
                            new List<ItemTask>
                            {
                                new MoneyChange(supplies.Copper)
                            },
                            new List<ulong>())
                    );
                }

                var completeId = (ushort) (quest.TemplateId / 64);
                if (!CompletedQuests.ContainsKey(completeId))
                    CompletedQuests.Add(completeId, new CompletedQuest(completeId));
                var complete = CompletedQuests[completeId];
                complete.Body.Set((int) (quest.TemplateId - completeId * 64), true);
                var body = new byte[8];
                complete.Body.CopyTo(body, 0);
                Owner.SendPacket(new SCQuestContextCompletedPacket(quest.TemplateId, body, res));
                Quests.Remove(questId);
                _removed.Add(questId);
                OnQuestComplete(questId);
            }
        }

        public void Drop(uint questId)
        {
            if (!Quests.ContainsKey(questId))
                return;
            var quest = Quests[questId];
            quest.Drop();
            Quests.Remove(questId);
        }

        public void OnQuestComplete(uint questId)
        {
            foreach(var quest in Quests.Values)
                quest.OnQuestComplete(questId);
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
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
        }
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterQuests
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private readonly List<uint> _removed;

        public Dictionary<uint, Quest> Quests { get; set; }
        public Dictionary<ushort, CompletedQuest> CompletedQuests { get; set; }

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
            if (Quests.ContainsKey(questId))
            {
                _log.Warn("Duplicate add quest {0}", questId);
                return;
            }

            var template = QuestManager.Instance.GetTemplate(questId);
            if (template == null)
                return;
            var quest = new Quest(template);
            quest.Id = Quests.Count + 1; // TODO временно, переделать
            quest.Status = QuestStatus.Progress;
            quest.Owner = Owner;
            Quests.Add(quest.TemplateId, quest);

            var res = quest.Start();
            if (res == 0)
                Quests.Remove(quest.TemplateId);
            else
                Owner.SendPacket(new SCQuestContextStartedPacket(quest, res));
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
                }

                var completeId = (ushort)(quest.TemplateId / 64);
                if (!CompletedQuests.ContainsKey(completeId))
                    CompletedQuests.Add(completeId, new CompletedQuest(completeId));
                var complete = CompletedQuests[completeId];
                complete.Body.Set((int)(quest.TemplateId - completeId * 64), true);
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
            _removed.Add(questId);
        }

        public void OnKill(Npc npc)
        {
            foreach (var quest in Quests.Values)
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
            foreach (var quest in Quests.Values)
                quest.OnItemUse(item);
        }

        public void OnInteraction(WorldInteractionType type)
        {
            foreach (var quest in Quests.Values)
                quest.OnInteraction(type);
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

        public void Load(GameDBContext ctx)
        {
            CompletedQuests = CompletedQuests.Concat(
                ctx.CompletedQuests
                .Where(c => c.Owner == Owner.Id)
                .ToList()
                .Select(c => (CompletedQuest)c)
                .ToDictionary(c => c.Id, c => c)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);

            Quests = Quests.Concat(
                ctx.Quests
                .Where(q => q.Owner == Owner.Id)
                .ToList()
                .Select(q => (Quest)q)
                .Select(q=> {
                    q.Owner = Owner;
                    q.RecalcObjectives(false);
                    return q;
                })
                .ToDictionary(q => q.TemplateId, q => q)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);
        }

        public void Save(GameDBContext ctx)
        {
            if (_removed.Count > 0)
            {
                ctx.Quests.RemoveRange(
                    ctx.Quests.Where(q => q.Owner == Owner.Id && _removed.Contains((uint)q.Id)));
                _removed.Clear();
            }
            ctx.SaveChanges();

            foreach (var value in CompletedQuests.Values)
            {
                ctx.CompletedQuests.RemoveRange(
                    ctx.CompletedQuests.Where(q => q.Id == value.Id && q.Owner == Owner.Id));
            }
            ctx.SaveChanges();
            ctx.CompletedQuests.AddRange(CompletedQuests.Values.Select(q => q.ToEntity(Owner.Id)));

            foreach (var value in Quests.Values)
            {
                ctx.Quests.RemoveRange(
                    ctx.Quests.Where(q => q.Id == value.Id && q.Owner == Owner.Id));
            }
            ctx.SaveChanges();

            ctx.Quests.AddRange(Quests.Values.Select(q => q.ToEntity()));
            ctx.SaveChanges();
        }
    }
}

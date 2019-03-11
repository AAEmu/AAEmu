using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests
{
    public class Quest : PacketMarshaler
    {
        public long Id { get; set; }
        public uint TemplateId { get; set; }
        public QuestTemplate Template { get; set; }
        public QuestStatus Status { get; set; }
        public int[] Objectives { get; set; }
        public byte Step { get; set; }
        public DateTime Time { get; set; }
        public Character Owner { get; set; }
        public int LeftTime => Time > DateTime.Now ? (int)(Time - DateTime.Now).TotalSeconds : -1;

        public uint GetActiveComponent()
        {
            return Template.GetComponent(Step).Id;
        }

        public Quest()
        {
            Objectives = new int[5];
        }

        public Quest(QuestTemplate template)
        {
            TemplateId = template.Id;
            Template = template;
            Objectives = new int[5];
        }

        public uint Start()
        {
            var res = false;
            var componentId = 0u;
            for (Step = 1; Step <= 8; Step++)
            {
                if (Step >= 6)
                    Status = QuestStatus.Ready;
                var component = Template.GetComponent(Step);
                if (component == null)
                    continue;
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                    res = acts[i].Use(Owner, this, Objectives[i]);
                if (!res)
                    return componentId;
                componentId = component.Id;
            }

            return res ? componentId : 0;
        }

        public void Update(bool send = true)
        {
            var res = false;
            var componentId = 0u;
            for (; Step <= 8; Step++)
            {
                if (Step >= 7)
                    Status = QuestStatus.Completed;
                else if (Step >= 6)
                    Status = QuestStatus.Ready;
                var component = Template.GetComponent(Step);
                if (component == null)
                    continue;
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                    res = acts[i].Use(Owner, this, Objectives[i]);
                if (!res)
                    break;
                componentId = component.Id;

                for (var i = 0; i < 5; i++)
                    Objectives[i] = 0;
            }

            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, componentId));
        }

        public uint Complete(int selected)
        {
            var res = false;
            var componentId = 0u;
            for (; Step <= 8; Step++)
            {
                if (Step >= 7)
                    Status = QuestStatus.Completed;
                var component = Template.GetComponent(Step);
                if (component == null)
                    continue;
                var acts = QuestManager.Instance.GetActs(component.Id);
                var selective = 0;
                for (var i = 0; i < acts.Length; i++)
                {
                    if (acts[i].DetailType == "QuestActSupplySelectiveItem")
                    {
                        selective++;
                        if (selective == selected)
                            res = acts[i].Use(Owner, this, Objectives[i]);
                    }
                    else
                        res = acts[i].Use(Owner, this, Objectives[i]);
                }

                if (!res)
                    return componentId;
                componentId = component.Id;
            }

            return res ? componentId : 0;
        }

        public void Drop()
        {
            Status = QuestStatus.Dropped;
            for (var i = 0; i < 5; i++)
                Objectives[i] = 0;
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));
            for (byte step = 0; step <= 8; step++)
            {
                var component = Template.GetComponent(step);
                if (component == null)
                    continue;
                var acts = QuestManager.Instance.GetActs(component.Id);
                foreach (var act in acts)
                {
                    var items = new List<(Item, int)>();
                    if (act.DetailType == "QuestActSupplyItem")
                    {
                        var template = act.GetTemplate<QuestActSupplyItem>();
                        if (template.DestroyWhenDrop)
                            items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));

                    }

                    if (act.DetailType == "QuestActObjItemGather")
                    {
                        var template = act.GetTemplate<QuestActObjItemGather>();
                        if (template.DestroyWhenDrop)
                            items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));
                    }

                    var tasks = new List<ItemTask>();
                    foreach (var (item, count) in items)
                    {
                        if (item.Count == 0)
                            tasks.Add(new ItemRemove(item));
                        else
                            tasks.Add(new ItemCountUpdate(item, -count));
                    }

                    Owner.SendPacket(
                        new SCItemTaskSuccessPacket(ItemTaskType.QuestRemoveSupplies, tasks, new List<ulong>())
                    );
                }
            }
        }

        public void OnKill(Npc npc)
        {
            var res = false;
            var component = Template.GetComponent(Step);
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    switch (act.DetailType)
                    {
                        case "QuestActObjMonsterHunt":
                        {
                            var template = acts[i].GetTemplate<QuestActObjMonsterHunt>();
                            if (template.NpcId == npc.TemplateId)
                            {
                                res = true;
                                Objectives[i]++;
                                if (Objectives[i] > template.Count) // TODO check to overtime
                                    Objectives[i] = template.Count;
                            }

                            break;
                        }
                        case "QuestActObjMonsterGroupHunt":
                        {
                            var template = acts[i].GetTemplate<QuestActObjMonsterGroupHunt>();
                            if (QuestManager.Instance.CheckGroupNpc(template.QuestMonsterGroupId, npc.TemplateId))
                            {
                                res = true;
                                Objectives[i]++;
                                if (Objectives[i] > template.Count) // TODO check to overtime
                                    Objectives[i] = template.Count;
                            }

                            break;
                        }
                    }
                }
            }

            if (res)
                Update();
        }

        public void OnItemGather(Item item, int count)
        {
            var res = false;
            var component = Template.GetComponent(Step);
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    switch (act.DetailType)
                    {
                        case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();
                            if (template.ItemId == item.TemplateId)
                            {
                                res = true;
                                Objectives[i] += count;
                                if (Objectives[i] > template.Count) // TODO check to overtime
                                    Objectives[i] = template.Count;
                            }

                            break;
                        }
                        case "QuestActObjItemGroupGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();
                            if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                            {
                                res = true;
                                Objectives[i] += count;
                                if (Objectives[i] > template.Count) // TODO check to overtime
                                    Objectives[i] = template.Count;
                            }

                            break;
                        }
                    }
                }
            }

            if (res)
                Update();
        }

        public void OnItemUse(Item item)
        {
            var res = false;
            var component = Template.GetComponent(Step);
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    switch (act.DetailType)
                    {
                        case "QuestActObjItemUse":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemUse>();
                            if (template.ItemId == item.TemplateId)
                            {
                                res = true;
                                Objectives[i]++;
                                if (Objectives[i] > template.Count) // TODO check to overtime
                                    Objectives[i] = template.Count;
                            }

                            break;
                        }
                        case "QuestActObjItemGroupUse":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupUse>();
                            if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                            {
                                res = true;
                                Objectives[i]++;
                                if (Objectives[i] > template.Count) // TODO check to overtime
                                    Objectives[i] = template.Count;
                            }

                            break;
                        }
                    }
                }
            }

            if (res)
                Update();
        }

        public void OnInteraction(WorldInteractionType type)
        {
            var res = false;
            var component = Template.GetComponent(Step);
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    if (act.DetailType == "QuestActObjInteraction")
                    {
                        var template = acts[i].GetTemplate<QuestActObjInteraction>();
                        if (template.WorldInteractionId == type)
                        {
                            res = true;
                            Objectives[i]++;
                            if (Objectives[i] > template.Count) // TODO check to overtime
                                Objectives[i] = template.Count;
                        }
                    }
                }
            }

            if (res)
                Update();
        }

        public void OnLevelUp()
        {
            var res = false;
            var component = Template.GetComponent(Step);
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    if (act.DetailType != "QuestActObjLevel")
                        continue;

                    var template = acts[i].GetTemplate<QuestActObjLevel>();
                    if (template.Level < Owner.Level)
                    {
                        res = true;
                        Objectives[i]++;
                    }
                }
            }

            if (res)
                Update();
        }

        public void OnQuestComplete(uint questId)
        {
            var res = false;
            var component = Template.GetComponent(Step);
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    if (act.DetailType == "QuestActObjCompleteQuest")
                    {
                        var template = act.GetTemplate<QuestActObjCompleteQuest>();
                        if (template.QuestId == questId)
                        {
                            res = true;
                            Objectives[i]++;
                        }
                    }
                }
            }

            if (res)
                Update();
        }

        public void RecalcObjectives(bool send = true)
        {
            var component = Template.GetComponent(Step);
            if (component == null)
                return;
            var acts = QuestManager.Instance.GetActs(component.Id);
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];
                if (act.DetailType == "QuestActObjItemGather")
                {
                    var template = acts[i].GetTemplate<QuestActObjItemGather>();
                    Objectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                    if (Objectives[i] > template.Count) // TODO check to overtime
                        Objectives[i] = template.Count;
                }

                if (act.DetailType == "QuestActObjItemGroupGather")
                {
                    var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();
                    Objectives[i] = 0;
                    foreach (var itemId in QuestManager.Instance.GetGroupItems(template.ItemGroupId))
                        Objectives[i] += Owner.Inventory.GetItemsCount(itemId);
                    if (Objectives[i] > template.Count) // TODO check to overtime
                        Objectives[i] = template.Count;
                }
            }

            Update(send);
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(TemplateId);
            stream.Write((byte)Status);
            foreach (var objective in Objectives) // TODO do-while, count 5
                stream.Write(objective);

            stream.Write(false); // isCheckSet
            stream.WriteBc(0);
            stream.Write(0u); // type(id)
            stream.WriteBc(0);
            stream.WriteBc(0);
            stream.Write(LeftTime);
            stream.Write(0u); // type(id)
            stream.Write(0L); // doodadId
            stream.Write(DateTime.MinValue); // acceptTime
            stream.Write((byte)0); // type
            stream.Write(0u); // acceptorType
            return stream;
        }

        public void ReadData(byte[] data)
        {
            var stream = new PacketStream(data);
            for (var i = 0; i < 5; i++)
                Objectives[i] = stream.ReadInt32();
            Step = stream.ReadByte();
            Time = stream.ReadDateTime();
        }

        public byte[] WriteData()
        {
            var stream = new PacketStream();
            foreach (var objective in Objectives)
                stream.Write(objective);
            stream.Write(Step);
            stream.Write(Time);
            return stream.GetBytes();
        }
    }
}

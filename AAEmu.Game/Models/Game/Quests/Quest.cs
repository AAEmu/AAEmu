using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;

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
        public int LeftTime => Time > DateTime.Now ? (int) (Time - DateTime.Now).TotalSeconds : -1;

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
                    res = acts[i].Use(Owner, Objectives[i]);
                if (!res)
                    return componentId;
                componentId = component.Id;
            }

            return res ? componentId : 0;
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
                            res = acts[i].Use(Owner, Objectives[i]);
                    }
                    else
                        res = acts[i].Use(Owner, Objectives[i]);
                }

                if (!res)
                    return componentId;
                componentId = component.Id;
            }

            return res ? componentId : 0;
        }

        public void Drop()
        {
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

        public void Update()
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
                    res = acts[i].Use(Owner, Objectives[i]);
                if (!res)
                    break;
                componentId = component.Id;
                for (var i = 0; i < 5; i++)
                    Objectives[i] = 0;
            }

            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, componentId));
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(TemplateId);
            stream.Write((byte) Status);
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
            return stream;
        }
    }
}

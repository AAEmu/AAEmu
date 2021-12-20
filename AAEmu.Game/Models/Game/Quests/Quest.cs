﻿using System;
using System.Collections.Generic;
using System.ComponentModel;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
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
        private const int OBJECTIVE_COUNT = 5;
        public Dictionary<QuestComponentKind, int[]> ObjectivesForStep { get; set; }

        private int[] CurrentObjectives
        {
            get
            {
                return ObjectivesForStep[Step];
            }
        }
        public QuestComponentKind Step { get; set; }
        public DateTime Time { get; set; }
        public Character Owner { get; set; }
        public int LeftTime => Time > DateTime.UtcNow ? (int)(Time - DateTime.UtcNow).TotalSeconds : -1;
        public int SupplyItem = 0;
        public bool LID = false;
        public bool EarlyCompletion = false;
        public long DoodadId { get; set; }
        public ulong ObjId { get; set; }
        public uint ComponentId { get; set; }
        public QuestAcceptorType QuestAcceptorType { get; set; }
        public uint AcceptorType { get; set; }

        public uint GetActiveComponent()
        {
            return Template.GetComponent(Step).Id;
        }

        public Quest()
        {
            ObjectivesForStep = new Dictionary<QuestComponentKind, int[]>();
        }

        public Quest(QuestTemplate template)
        {
            TemplateId = template.Id;
            Template = template;
            ObjectivesForStep = new Dictionary<QuestComponentKind, int[]>();
        }

        public uint Start()
        {
            ObjId = 0;

            var res = false;
            var component = GetFirstComponent(); // в начале берем всегда первый компонент
            CheckThatTheNextStepIsReady();
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                switch (acts[i].DetailType)
                {
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();
                            // TODO: Check both inventory and warehouse
                            Owner.Inventory.Bag.GetAllItemsByTemplate(template.Id, -1, out _, out var objectivesCounted);
                            CurrentObjectives[i] = objectivesCounted;
                            //CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                            {
                                CurrentObjectives[i] = template.Count;
                            }

                            _log.Warn("Quest: step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status);//  for debuging
                            res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                            break;
                        }
                    case "QuestActConReportNpc":
                        res = false;
                        break;
                    case "QuestActObjItemUse":
                        res = false;
                        break;
                    case "QuestActSupplyItem" when component.KindId == QuestComponentKind.Supply:
                        res = acts[i].Use(Owner, this, SupplyItem);
                        break;
                    case "QuestActConAcceptNpc":
                    case "QuestActConAcceptDoodad":
                        res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                        break;
                    default:
                        res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                        break;
                }

                _log.Warn("Quest: step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status);//  for debuging

            }

            if (!res)
            {
                return component.Id;
            }
            return res ? component.Id : 0;
        }

        public void Update(bool send = true)
        {
            if (!send) { return; }

            var res = false;
            var componentId = 0u;
            for (; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step >= QuestComponentKind.Drop)
                {
                    Status = QuestStatus.Completed;
                }
                else if (Step >= QuestComponentKind.Ready)
                {
                    Status = QuestStatus.Ready;
                }

                var components = Template.GetComponents(Step);
                switch (components.Count)
                {
                    case 0 when Step == QuestComponentKind.Ready:
                        Owner.Quests.Complete(TemplateId, 0);
                        continue;
                    case 0:
                        continue;
                }
                int c;
                for (c = 0; c <= components.Count - 1; c++)
                {
                    var acts = QuestManager.Instance.GetActs(components[c].Id);
                    for (var i = 0; i < acts.Length; i++)
                    {
                        switch (acts[i].DetailType)
                        {
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    var next = Step;
                                    next++;
                                    var componentnext = Template.GetComponent(next);
                                    if (componentnext == null) break;
                                    var actsnext = QuestManager.Instance.GetActs(componentnext.Id);
                                    foreach (var qa in actsnext)
                                    {
                                        var questSupplyItem = (QuestActSupplyItem)QuestManager.Instance.GetActTemplate(acts[i].DetailId, "QuestActSupplyItem");
                                        var questItemGather = (QuestActObjItemGather)QuestManager.Instance.GetActTemplate(qa.DetailId, "QuestActObjItemGather");
                                        switch (qa.DetailType)
                                        {
                                            case "QuestActObjItemGather" when (questSupplyItem.ItemId == questItemGather.ItemId):
                                                res = acts[i].Use(Owner, this, SupplyItem);
                                                break;
                                            default:
                                                res = false;
                                                break;
                                        }
                                    }

                                    break;
                                } 
                            case "QuestActObjMonsterGroupHunt":
                            {
                                res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                                break;
                            }
                            case "QuestActConReportNpc":
                                res = false;
                                break;
                            default:
                                res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                                break;
                        }
                        SupplyItem = 0;
                        _log.Warn("Quest: {0} {1} {2}", Step, res, acts[i].DetailType); // for debuging
                    }
                }
                if (!res)
                {
                    break;
                }
                componentId = components[c - 1].Id;
            }
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, componentId));
        }

        public uint Complete(int selected)
        {
            var res = false;
            var component = GetNextComponent(); // возьмем следующий компонент
            //var component = GetCurrentComponent(); // возьмем компонент
            CheckThatTheNextStepIsReady();
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            var selective = 0;
            for (var i = 0; i < acts.Length; i++)
            {
                switch (acts[i].DetailType)
                {
                    case "QuestActSupplySelectiveItem":
                        {
                            selective++;
                            if (selective == selected)
                            {
                                res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                            }

                            break;
                        }
                    case "QuestActSupplyItem":
                        res = acts[i].Use(Owner, this, SupplyItem);
                        break;
                    case "QuestActConAutoComplete":
                        res = true;
                        break;
                    default:
                        res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                        bool CStep = Template.LetItDone;
                        if (CStep == true && res == false)
                        {
                            EarlyCompletion = true;
                            res = true;
                            break;
                        }
                        break;
                }

                SupplyItem = 0;

                _log.Warn("Quest: step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status);//  for debuging
            }

            if (!res)
            {
                return component.Id;
            }
            return res ? component.Id : 0;

        }
        public int GetCustomExp() { return GetCustomSupplies("exp"); }

        public int GetCustomCopper() { return GetCustomSupplies("copper"); }

        public int GetCustomSupplies(string supply)
        {
            var value = 0;
            var component = Template.GetComponent(QuestComponentKind.Reward);
            if (component == null)
            {
                return 0;
            }

            var acts = QuestManager.Instance.GetActs(component.Id);
            foreach (var act in acts)
            {
                switch (act.DetailType)
                {
                    case "QuestActSupplyExp" when supply == "exp":
                        {
                            var template = act.GetTemplate<QuestActSupplyExp>();
                            value = template.Exp;
                            break;
                        }
                    case "QuestActSupplyCoppers" when supply == "copper":
                        {
                            var template = act.GetTemplate<QuestActSupplyCopper>();
                            value = template.Amount;
                            break;
                        }
                    default:
                        value = 0;
                        break;
                }
            }
            return value;
        }

        public void RemoveQuestItems()
        {
            for (Step = QuestComponentKind.None; Step <= QuestComponentKind.Reward; Step++)
            {
                var component = Template.GetComponent(Step);
                if (component == null)
                {
                    continue;
                }

                var acts = QuestManager.Instance.GetActs(component.Id);
                foreach (var act in acts)
                {
                    var items = new List<(Item, int)>();
                    if (act.DetailType == "QuestActSupplyItem" && Step == QuestComponentKind.Supply)
                    {
                        var template = act.GetTemplate<QuestActSupplyItem>();
                        if (template.DestroyWhenDrop)
                        {
                            Owner.Inventory.TakeoffBackpack(ItemTaskType.QuestRemoveSupplies);
                        }

                        Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, template.Count, null);
                        //items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));
                    }
                    if (act.DetailType == "QuestActObjItemGather")
                    {
                        var template = act.GetTemplate<QuestActObjItemGather>();
                        if (template.DestroyWhenDrop)
                        {
                            Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, template.Count, null);
                            //items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));
                        }
                    }
                    /*
                    var tasks = new List<ItemTask>();
                    foreach (var (item, count) in items)
                    {
                        if (item.Count == 0)
                        {
                            tasks.Add(new ItemRemove(item));
                        }
                        else
                        {
                            tasks.Add(new ItemCountUpdate(item, -count));
                        }
                    }
                    Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.QuestRemoveSupplies, tasks, new List<ulong>()));
                    */
                }
            }
        }

        public void Drop(bool update)
        {
            Status = QuestStatus.Dropped;
            Step = QuestComponentKind.Drop;
            for (var i = 0; i < OBJECTIVE_COUNT; i++)
            {
                CurrentObjectives[i] = 0;
            }
            if (update)
                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));
            RemoveQuestItems();
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
                                    CurrentObjectives[i]++;
                                    if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                    {
                                        CurrentObjectives[i] = template.Count;
                                    }
                                }

                                break;
                            }
                        case "QuestActObjMonsterGroupHunt":
                            {
                                var template = acts[i].GetTemplate<QuestActObjMonsterGroupHunt>();
                                if (QuestManager.Instance.CheckGroupNpc(template.QuestMonsterGroupId, npc.TemplateId))
                                {
                                    res = true;
                                    CurrentObjectives[i]++;
                                    if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                    {
                                        CurrentObjectives[i] = template.Count;
                                    }
                                }

                                break;
                            }
                    }
                }
            }
            Update(res);
        }

        public void OnItemGather(Item item, int count)
        {
            var res = false;
            var component = Template.GetComponents(Step);
            for (var c = 1; c <= component.Count - 1; c++)
            {
                if (component != null)
                {
                    var acts = QuestManager.Instance.GetActs(component[c].Id);
                    for (var i = 0; i < acts.Length; i++)
                    {
                        var act = acts[i];
                        switch (act.DetailType)
                        {
                            case "QuestActObjMonsterGroupHunt":
                                {
                                    res = false;
                                    break;
                                }
                            case "QuestActSupplyItem":
                                {
                                    var template = acts[i].GetTemplate<QuestActSupplyItem>();
                                    if (template.ItemId == item.TemplateId)
                                    {
                                        res = true;
                                        SupplyItem += count;
                                        if (SupplyItem > template.Count) // TODO check to overtime
                                        {
                                            SupplyItem = template.Count;
                                        }
                                    }

                                    break;
                                }
                            case "QuestActObjItemGather":
                                {
                                    var template = acts[i].GetTemplate<QuestActObjItemGather>();
                                    if (template.ItemId == item.TemplateId)
                                    {
                                        res = true;
                                        CurrentObjectives[i] += count;
                                        if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                        {
                                            CurrentObjectives[i] = template.Count;
                                        }
                                    }

                                    break;
                                }
                            case "QuestActObjItemGroupGather":
                                {
                                    var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();
                                    if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                    {
                                        res = true;
                                        CurrentObjectives[i] += count;
                                        if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                        {
                                            CurrentObjectives[i] = template.Count;
                                        }
                                    }

                                    break;
                                }
                        }
                    }
                }
            }

            Update(res);
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
                                    CurrentObjectives[i]++;
                                    if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                    {
                                        CurrentObjectives[i] = template.Count;
                                    }
                                }

                                break;
                            }
                        case "QuestActObjItemGroupUse":
                            {
                                var template = acts[i].GetTemplate<QuestActObjItemGroupUse>();
                                if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    res = true;
                                    CurrentObjectives[i]++;
                                    if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                    {
                                        CurrentObjectives[i] = template.Count;
                                    }
                                }

                                break;
                            }
                    }
                }
            }

            Update(res);
        }

        public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
        {
            var res = false;

            var component = GetNextComponent(); // возьмем следующий компонент
            CheckThatTheNextStepIsReady();
            //var component = GetCurrentComponent(); // возьмем компонент
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    if (act.DetailType == "QuestActObjInteraction")
                    {
                        var template = acts[i].GetTemplate<QuestActObjInteraction>();
                        if (template.WorldInteractionId == type)
                        {
                            var interactionTarget = (Doodad)target;
                            if (template.DoodadId == interactionTarget.TemplateId)
                            {
                                ObjId = interactionTarget.ObjId;

                                res = true;
                                CurrentObjectives[i]++;
                                if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                }
                            }
                        }
                    }
                    if (act.DetailType == "QuestActObjItemGather")
                    {
                        var template = acts[i].GetTemplate<QuestActObjItemGather>();

                        var items = new List<Item>();
                        if (target != null)
                        {
                            var interactionTarget = (Doodad)target;
                            ObjId = interactionTarget.ObjId;
                        }

                        CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                        if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                        {
                            CurrentObjectives[i] = template.Count;
                            res = false;
                        }
                    }
                }
            }

            if (!res)
            {
                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, component.Id));
            }
            Update(res);
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
                    {
                        continue;
                    }

                    var template = acts[i].GetTemplate<QuestActObjLevel>();
                    if (template.Level >= Owner.Level)
                    {
                        continue;
                    }

                    res = true;
                    CurrentObjectives[i]++;
                }
            }

            Update(res);
        }

        public void OnQuestComplete(uint questContextId)
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
                        case "QuestActObjCompleteQuest":
                            {
                                var template = act.GetTemplate<QuestActObjCompleteQuest>();
                                if (template.QuestId == questContextId)
                                {
                                    res = true;
                                    CurrentObjectives[i]++;
                                }

                                break;
                            }
                    }
                }
            }

            Update(res);
        }

        public void RecalcObjectives(bool send = true)
        {
            var component = Template.GetComponent(Step);
            if (component == null)
            {
                return;
            }

            var acts = QuestManager.Instance.GetActs(component.Id);
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];

                switch (act.DetailType)
                {
                    case "QuestActSupplyItem":
                        {
                            var template = acts[i].GetTemplate<QuestActSupplyItem>();
                            CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                            {
                                CurrentObjectives[i] = template.Count;
                            }

                            break;
                        }
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();
                            CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                            {
                                CurrentObjectives[i] = template.Count;
                            }

                            break;
                        }
                    case "QuestActObjItemGroupGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();
                            CurrentObjectives[i] = 0;
                            foreach (var itemId in QuestManager.Instance.GetGroupItems(template.ItemGroupId))
                            {
                                CurrentObjectives[i] += Owner.Inventory.GetItemsCount(itemId);
                            }

                            if (CurrentObjectives[i] > template.Count) // TODO check to overtime
                            {
                                CurrentObjectives[i] = template.Count;
                            }

                            break;
                        }
                }
            }

            Update(send);
        }

        public void ClearObjectives()
        {
            for (var i = QuestComponentKind.None; i <= QuestComponentKind.Reward; i++)
            {
                ObjectivesForStep.TryAdd(i, new[] { 0, 0, 0, 0, 0 });
            }
        }

        public int[] GetCurrentObjectives(QuestComponentKind step)
        {
            return CurrentObjectives;
        }

        public void CheckThatTheNextStepIsReady()
        {
            for (var step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
            {
                var component = Template.GetComponent(step);
                if (component == null) // пропускаем пустые
                    continue;

                if (component.KindId <= Step) // находим компонент следующий за текущим
                    continue;

                Status = component.KindId == QuestComponentKind.Ready ? QuestStatus.Ready : QuestStatus.Progress;
                //    SetStatus();
                return;
            }

            return; // нет следующего компонента
        }

        public QuestComponent GetFirstComponent()
        {
            for (var step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
            {
                var component = Template.GetComponent(step);
                if (component == null)
                    continue;

                Step = component.KindId; // указываем на первый шаг
                //SetStatus();
                return component;
            }

            return null; // нет следующего компонента
        }

        public QuestComponent GetNextComponent()
        {
            for (var step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
            {
                var component = Template.GetComponent(step);
                if (component == null) // пропускаем пустые
                    continue;

                if (component.KindId <= Step) // находим компонент следующий за текущим
                    continue;

                Step = component.KindId; // указываем на следующий шаг
                //    SetStatus();
                return component;
            }

            return null; // нет следующего компонента
        }

        public QuestComponent GetCurrentComponent()
        {
            var component = Template.GetComponent(Step);
            if (component == null)
                return null; // нет компонента

            Step = component.KindId; // указываем на текущий шаг
            //SetStatus();

            return component;
        }

        private void SetStatus()
        {
            return;
            switch (Step)
            {
                case QuestComponentKind.None:
                    Status = QuestStatus.Invalid;
                    break;
                case QuestComponentKind.Start:
                    Status = QuestStatus.Progress;
                    break;
                case QuestComponentKind.Supply:
                    Status = QuestStatus.Progress;
                    break;
                case QuestComponentKind.Progress:
                    Status = QuestStatus.Progress;
                    break;
                case QuestComponentKind.Ready:
                    Status = QuestStatus.Ready;
                    break;
                case QuestComponentKind.Fail:
                    Status = QuestStatus.Failed;
                    break;
                case QuestComponentKind.Drop:
                    Status = QuestStatus.Dropped;
                    break;
                case QuestComponentKind.Reward:
                    Status = QuestStatus.Progress;
                    break;
                default:
                    break;
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(TemplateId);
            stream.Write((byte)Status);
            foreach (var objective in CurrentObjectives) // TODO do-while, count 5
            {
                stream.Write(objective);
            }

            stream.Write(false);         // isCheckSet
            stream.WriteBc((uint)ObjId); // ObjId
            stream.Write(0u);            // type(id)
            stream.WriteBc((uint)ObjId); // ObjId
            stream.WriteBc((uint)ObjId); // ObjId
            stream.Write(LeftTime);
            stream.Write(0u);                      // type(id)
            stream.Write(DoodadId);                // doodadId
            stream.Write(DateTime.UtcNow);         // acceptTime
            stream.Write((byte)QuestAcceptorType); // type QuestAcceptorType
            stream.Write(AcceptorType);            // acceptorType npcId or doodadId
            return stream;
        }

        public void ReadData(byte[] data)
        {
            var stream = new PacketStream(data);

            for (var i = QuestComponentKind.None; i <= QuestComponentKind.Reward; i++)
            {
                ObjectivesForStep.TryAdd(i, new[] { 0, 0, 0, 0, 0 });
                for (var objIdx = 0; objIdx < OBJECTIVE_COUNT; objIdx++)
                    ObjectivesForStep[i][objIdx] = stream.ReadInt32();
            }

            Step = (QuestComponentKind)stream.ReadByte();
            Time = stream.ReadDateTime();
        }

        public byte[] WriteData()
        {
            var stream = new PacketStream();

            for (var i = QuestComponentKind.None; i <= QuestComponentKind.Reward; i++)
                foreach (var value in ObjectivesForStep[i])
                    stream.Write(value);

            stream.Write((byte)Step);
            stream.Write(Time);
            return stream.GetBytes();
        }
    }
}

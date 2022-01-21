using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.v2;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Quests;

using NLog;

namespace AAEmu.Game.Models.Game.Quests
{
    public class Quest : PacketMarshaler
    {
        private const int ObjectiveCount = 5;

        public long Id { get; set; }
        public uint TemplateId { get; set; }
        public QuestTemplate Template { get; set; }
        public QuestStatus Status { get; set; }
        public int[] Objectives { get; set; }
        public QuestComponentKind Step { get; set; }
        public DateTime Time { get; set; }
        public Character Owner { get; set; }
        public int LeftTime => Time > DateTime.UtcNow ? (int)(Time - DateTime.UtcNow).TotalSeconds : -1;
        public int SupplyItem = 0;
        public bool EarlyCompletion { get; set; }
        public long DoodadId { get; set; }
        public long ObjId { get; set; }
        public uint ComponentId { get; set; }
        public QuestAcceptorType QuestAcceptorType { get; set; }
        public uint AcceptorType { get; set; }
        public QuestCompleteTask QuestTask { get; set; }


        public uint GetActiveComponent()
        {
            return Template.GetComponent(Step).Id;
        }

        public Quest()
        {
            Objectives = new int[ObjectiveCount];
            SupplyItem = 0;
            EarlyCompletion = false;
            ObjId = 0;
        }

        public Quest(QuestTemplate template)
        {
            TemplateId = template.Id;
            Template = template;
            Objectives = new int[ObjectiveCount];
            SupplyItem = 0;
            EarlyCompletion = false;
            ObjId = 0;
        }

        public uint Start()
        {
            var res = false;
            for (Step = QuestComponentKind.None; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step >= QuestComponentKind.Ready)
                    Status = QuestStatus.Ready;

                var components = Template.GetComponents(Step);
                if (components.Length == 0 || Step == QuestComponentKind.Fail || Step == QuestComponentKind.Drop)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    for (var i = 0; i < acts.Length; i++)
                    {
                        switch (acts[i].DetailType)
                        {
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    res = acts[i].Use(Owner, this, SupplyItem);
                                    var next = QuestComponentKind.Progress;
                                    var componentnext = Template.GetComponent(next);
                                    if (componentnext == null)
                                        break;
                                    var actsnext = QuestManager.Instance.GetActs(componentnext.Id);
                                    foreach (var qa in actsnext)
                                    {
                                        var questSupplyItem = (QuestActSupplyItem)QuestManager.Instance.GetActTemplate(acts[i].DetailId, "QuestActSupplyItem");
                                        var questItemGather = (QuestActObjItemGather)QuestManager.Instance.GetActTemplate(qa.DetailId, "QuestActObjItemGather");
                                        switch (qa.DetailType)
                                        {
                                            case "QuestActObjItemGather" when questSupplyItem.ItemId == questItemGather.ItemId:
                                                Owner.Inventory.Bag.GetAllItemsByTemplate(questSupplyItem.ItemId, -1, out _, out Objectives[componentIndex]);
                                                res = qa.Use(Owner, this, Objectives[componentIndex]);
                                                Step = next;
                                                ComponentId = components[componentIndex].Id;
                                                break;
                                            default:
                                                res = false;
                                                break;
                                        }
                                    }
                                    break;
                                }
                            case "QuestActObjItemGather":
                                {
                                    var template = acts[i].GetTemplate<QuestActObjItemGather>();
                                    // TODO: Check both inventory and warehouse
                                    Owner.Inventory.Bag.GetAllItemsByTemplate(template.ItemId, -1, out _, out Objectives[componentIndex]);
                                    //Objectives[c] = Owner.Inventory.GetItemsCount(template.ItemId);
                                    res = acts[i].Use(Owner, this, Objectives[componentIndex]);
                                    ComponentId = components[componentIndex].Id;
                                    break;
                                }
                            case "QuestActConReportNpc":
                            case "QuestActConAutoComplete":
                            case "QuestActObjItemUse":
                                res = false;
                                break;
                            case "QuestActSupplyItem":
                                res = acts[i].Use(Owner, this, SupplyItem);
                                ComponentId = components[componentIndex].Id;
                                break;
                            default:
                                res = acts[i].Use(Owner, this, Objectives[componentIndex]);
                                ComponentId = components[componentIndex].Id;
                                break;
                        }
                        _log.Warn("[Quest] Start: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, acts[i].DetailType);
                    }
                }
                if (!res)
                {
                    return ComponentId;
                }
            }
            return res ? ComponentId : 0;
        }

        /// <summary>
        /// Метод предназначен для вызова из скрита QuestCmd, команда /quest add <questId>
        /// </summary>
        /// <returns></returns>
        public uint StartFirstOnly()
        {
            var res = false;
            for (Step = QuestComponentKind.None; Step <= QuestComponentKind.Start; Step++)
            {
                var components = Template.GetComponents(Step);
                if (components.Length == 0)
                {
                    continue;
                }
                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    for (var i = 0; i < acts.Length; i++)
                    {
                        switch (acts[i].DetailType)
                        {
                            default:
                                res = acts[i].Use(Owner, this, Objectives[componentIndex]);
                                ComponentId = components[componentIndex].Id;
                                _log.Warn("[Quest] Start: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, acts[i].DetailType);
                                break;
                        }
                    }
                }
                if (!res)
                {
                    return ComponentId;
                }
            }
            return res ? ComponentId : 0;
        }

        public void Update(bool send = true)
        {
            if (!send) { return; }

            var res = false;
            for (; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step == QuestComponentKind.Fail || Step == QuestComponentKind.Drop)
                    continue;

                if (Step >= QuestComponentKind.Drop)
                    Status = QuestStatus.Completed;
                else if (Step >= QuestComponentKind.Ready)
                    Status = QuestStatus.Ready;

                var components = Template.GetComponents(Step);
                switch (components.Length)
                {
                    case 0 when Step == QuestComponentKind.Ready: // если нет шага Ready переходим к завершению квеста
                        {
                            // делаем задержку 6 сек перед вызовом Owner.Quests.Complete(TemplateId, 0);
                            var delay = 6;
                            QuestTask = new QuestCompleteTask(Owner, TemplateId);
                            TaskManager.Instance.Schedule(QuestTask, TimeSpan.FromSeconds(delay));
                        }
                        continue;
                    case 0: // пропустим пустые шаги
                        continue;
                }
                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    for (var i = 0; i < acts.Length; i++)
                    {
                        switch (acts[i].DetailType)
                        {
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    res = acts[i].Use(Owner, this, SupplyItem);
                                    var next = QuestComponentKind.Progress;
                                    var componentnext = Template.GetComponent(next);
                                    if (componentnext == null)
                                        break;
                                    var actsnext = QuestManager.Instance.GetActs(componentnext.Id);
                                    foreach (var qa in actsnext)
                                    {
                                        var questSupplyItem = (QuestActSupplyItem)QuestManager.Instance.GetActTemplate(acts[i].DetailId, "QuestActSupplyItem");
                                        var questItemGather = (QuestActObjItemGather)QuestManager.Instance.GetActTemplate(qa.DetailId, "QuestActObjItemGather");
                                        switch (qa.DetailType)
                                        {
                                            case "QuestActObjItemGather" when questSupplyItem.ItemId == questItemGather.ItemId:
                                                Owner.Inventory.Bag.GetAllItemsByTemplate(questSupplyItem.ItemId, -1, out _, out Objectives[componentIndex]);
                                                res = qa.Use(Owner, this, Objectives[componentIndex]);
                                                Step = next;
                                                ComponentId = components[componentIndex].Id;
                                                break;
                                            default:
                                                res = false;
                                                break;
                                        }
                                    }
                                    break;
                                }
                            case "QuestActConReportNpc":
                            case "QuestActConAutoComplete":
                                res = acts[i].Use(Owner, this, Objectives[componentIndex]);
                                // проверка результатов на валидность
                                if (res)
                                {
                                    // компонент - выполнен
                                    Status = QuestStatus.Ready;
                                    Owner.Quests.Complete(TemplateId, 0);
                                    return;
                                }
                                break;
                            case "QuestActObjSphere":
                                {
                                    // подготовим работу QuestSphere
                                    var template = acts[i].GetTemplate<QuestActObjSphere>();
                                    Status = QuestStatus.Progress;
                                    ComponentId = components[componentIndex].Id;

                                    var sphereQuestTrigger = new SphereQuestTrigger();
                                    sphereQuestTrigger.Sphere = SphereQuestManager.Instance.GetQuestSpheres(components[componentIndex].Id);
                                    sphereQuestTrigger.Owner = Owner;
                                    sphereQuestTrigger.Quest = this;
                                    sphereQuestTrigger.TickRate = 500;

                                    SphereQuestManager.Instance.AddSphereQuestTrigger(sphereQuestTrigger);
                                    var Duration = 500;
                                    if (Duration > 0)
                                    {
                                        // TODO : Add a proper delay in here
                                        Task.Run(async () =>
                                        {
                                            await Task.Delay(Duration);
                                        });
                                    }
                                    break;
                                }
                                case "QuestActObjItemGather":
                                {
                                    res = acts[i].Use(Owner, this, Objectives[componentIndex]);
                                    // проверка результатов на валидность
                                    if (!Template.Selective)
                                    {
                                        if (res)
                                        {
                                            // компонент - выполнен
                                            Status = QuestStatus.Ready;
                                            ComponentId = components[componentIndex].Id;
                                        }
                                        else
                                        {
                                            // компонент - в процессе выполнения
                                            Status = QuestStatus.Progress;
                                            ComponentId = 0;
                                            // если res == false, также надо слать пакет SCQuestContextUpdatedPacket
                                        }
                                    }
                                    else
                                    {
                                        // компонент - выполнен
                                        res = true;
                                        Status = QuestStatus.Ready;
                                        ComponentId = components[componentIndex].Id;
                                    }
                                    break;
                                }
                            default:
                                //case "QuestActObjItemGather":
                                //case "QuestActObjMonsterHunt":
                                //case "QuestActObjMonsterGroupHunt":
                                // эти акты всегда парные! ItemGather & MonsterHunt, ItemGather & MonsterGroupHunt
                                {
                                    res = acts[i].Use(Owner, this, Objectives[componentIndex]);
                                    // проверка результатов на валидность
                                    if (res)
                                    {
                                        // компонент - выполнен
                                        Status = QuestStatus.Ready;
                                        ComponentId = components[componentIndex].Id;
                                    }
                                    else
                                    {
                                        // компонент - в процессе выполнения
                                        Status = QuestStatus.Progress;
                                        ComponentId = 0;
                                        // если res == false, также надо слать пакет SCQuestContextUpdatedPacket
                                        //Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                    }
                                    break;
                                }
                        }
                        SupplyItem = 0;
                        _log.Warn("[Quest] Update: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, acts[i].DetailType);
                    }
                }
                if (!res)
                {
                    break;
                }
            }
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
        }

        public uint Complete(int selected)
        {
            var res = false;
            var Step = QuestComponentKind.Ready; // покажем, что заканчиваем квест
            for (; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step >= QuestComponentKind.Drop)
                    Status = QuestStatus.Completed;

                var components = Template.GetComponents(Step);
                if (components.Length == 0)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    if (Step == QuestComponentKind.Ready)
                        ComponentId = components[componentIndex].Id;

                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    var selective = 0;
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActConReportNpc":
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                break;
                            case "QuestActSupplySelectiveItem":
                                {
                                    selective++;
                                    if (selective == selected)
                                    {
                                        res = act.Use(Owner, this, Objectives[componentIndex]);
                                        if (ComponentId == 0)
                                            ComponentId = components[componentIndex].Id;
                                    }
                                    break;
                                }
                            case "QuestActSupplyItem":
                                res = act.Use(Owner, this, SupplyItem);
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                break;
                            case "QuestActConAutoComplete":
                                res = true;
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                break;
                            default:
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                var CStep = Template.LetItDone;
                                if (CStep && res == false)
                                {
                                    EarlyCompletion = true;
                                    res = true;
                                }
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                break;
                        }
                        SupplyItem = 0;
                        _log.Warn("[Quest] Complete: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                    }
                    if (!res)
                    {
                        break;
                    }
                }
                if (!res)
                    return ComponentId;
            }
            return res ? ComponentId : 0;
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
                _log.Warn("[Quest] GetCustomSupplies: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
            }
            return value;
        }

        public void RemoveQuestItems()
        {
            for (var step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
            {
                var component = Template.GetComponent(step);
                if (component == null)
                {
                    continue;
                }

                var acts = QuestManager.Instance.GetActs(component.Id);
                foreach (var act in acts)
                {
                    var items = new List<(Item, int)>();
                    if (act.DetailType == "QuestActSupplyItem" && step == QuestComponentKind.Supply)
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
                    _log.Warn("[Quest] RemoveQuestItems: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
                }
            }
        }

        public void Drop(bool update)
        {
            Status = QuestStatus.Dropped;
            Step = QuestComponentKind.Drop;
            for (var i = 0; i < ObjectiveCount; i++)
                Objectives[i] = 0;

            if (update)
                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));

            RemoveQuestItems();
        }

        public void OnReportToNpc(Npc npc, int selected)
        {
            var res = false;
            Step = QuestComponentKind.Ready;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                var selective = 0;
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActConReportNpc":
                            {
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                // проверка результатов на валидность
                                if (res)
                                {
                                    // компонент - выполнен
                                    Status = QuestStatus.Ready;
                                    Owner.Quests.Complete(TemplateId, selected);
                                    return;
                                }
                                break;
                            }
                        case "QuestActSupplySelectiveItem":
                            {
                                selective++;
                                if (selective == selected)
                                {
                                    res = act.Use(Owner, this, Objectives[componentIndex]);
                                    if (ComponentId == 0)
                                        ComponentId = components[componentIndex].Id;
                                }
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnReportToNpc: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnReportToDoodad(Doodad doodad)
        {
            var res = false;
            Step = QuestComponentKind.Ready;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActConReportDoodad":
                            {
                                var template = act.GetTemplate<QuestActConReportDoodad>();
                                if (template.DoodadId == doodad.TemplateId)
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnReportToDoodad: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnTalkMade(Npc npc)
        {
            var res = false;
            Step = QuestComponentKind.Progress;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActObjTalk":
                            {
                                var template = act.GetTemplate<QuestActObjTalk>();
                                if (template.NpcId == npc.TemplateId)
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnTalkMade: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnKill(Npc npc)
        {
            var res = false;
            Step = QuestComponentKind.Progress;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActObjMonsterHunt":
                            {
                                var template = act.GetTemplate<QuestActObjMonsterHunt>();
                                if (template.NpcId == npc.TemplateId)
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }

                                break;
                            }
                        case "QuestActObjMonsterGroupHunt":
                            {
                                var template = act.GetTemplate<QuestActObjMonsterGroupHunt>();
                                if (QuestManager.Instance.CheckGroupNpc(template.QuestMonsterGroupId, npc.TemplateId))
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }

                                break;
                            }
                    }
                    _log.Warn("[Quest] OnKill: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnItemGather(Item item, int count)
        {
            var res = false;
            Step = QuestComponentKind.Progress;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActSupplyItem":
                            {
                                var template = act.GetTemplate<QuestActSupplyItem>();
                                if (template.ItemId == item.TemplateId)
                                {
                                    res = true;
                                    SupplyItem += count;
                                }
                                break;
                            }
                        case "QuestActObjItemGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGather>();
                                if (template.ItemId == item.TemplateId)
                                {
                                    res = true;
                                    Objectives[componentIndex] += count;
                                }
                                break;
                            }
                        case "QuestActObjItemGroupGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupGather>();
                                if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    res = true;
                                    Objectives[componentIndex] += count;
                                }
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnItemGather: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnItemUse(Item item)
        {
            var res = false;
            Step = QuestComponentKind.Progress;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActObjItemUse":
                            {
                                var template = act.GetTemplate<QuestActObjItemUse>();
                                if (template.ItemId == item.TemplateId)
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }

                                break;
                            }
                        case "QuestActObjItemGroupUse":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupUse>();
                                if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }

                                break;
                            }
                    }
                    _log.Warn("[Quest] OnItemUse: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
        {
            var res = false;
            Step = QuestComponentKind.Progress;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            var interactionTarget = (Doodad)target;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActObjInteraction":
                            {
                                var template = act.GetTemplate<QuestActObjInteraction>();
                                if (template.WorldInteractionId == type)
                                {
                                    if (template.DoodadId == interactionTarget.TemplateId)
                                    {
                                        res = true;
                                        Objectives[componentIndex]++;
                                    }
                                }
                                break;
                            }
                        case "QuestActObjItemUse":
                            {
                                var template = act.GetTemplate<QuestActObjItemUse>();
                                if (template.ItemId == interactionTarget.ItemTemplateId)
                                {
                                    res = true;
                                    Objectives[componentIndex]++;
                                }
                                //List<Item> item;
                                //Owner.Inventory.Bag.GetAllItemsByTemplate(template.ItemId, -1, out item, out Objectives[componentIndex]);
                                //if (Objectives[componentIndex] > 0)
                                //{
                                //    res = true;
                                //    Objectives[componentIndex]++; // TODO проверить нужен он здесь?
                                //}
                                break;
                            }
                        case "QuestActObjItemGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGather>();
                                Objectives[componentIndex] = Owner.Inventory.GetItemsCount(template.ItemId);
                                if (Objectives[componentIndex] > 0)
                                {
                                    res = true;
                                    //Objectives[componentIndex]++;
                                }
                                break;
                            }
                        case "QuestActObjItemGroupUse":
                            {
                                //var template = act.GetTemplate<QuestActObjItemGroupUse>();
                                //List<Item> item;
                                //Owner.Inventory.Bag.GetAllItemsByTemplate(template.ItemGroupId, -1, out item, out Objectives[componentIndex]);
                                //if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item[0].TemplateId))
                                //{
                                res = true;
                                Objectives[componentIndex]++; // TODO проверить нужен он здесь?
                                                              //}
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnInteraction: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
        }

        public void OnLevelUp()
        {
            var res = false;
            Step = QuestComponentKind.Progress;
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
                    Objectives[i]++;
                    _log.Warn("[Quest] OnLevelUp: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }
            Update(res);
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
                    switch (act.DetailType)
                    {
                        case "QuestActObjCompleteQuest":
                            {
                                var template = act.GetTemplate<QuestActObjCompleteQuest>();
                                if (template.QuestId == questId)
                                {
                                    res = true;
                                    Objectives[i]++;
                                }

                                break;
                            }
                    }
                    _log.Warn("[Quest] OnQuestComplete: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                }
            }

            Update(res);
        }

        public void OnEnterSphere(SphereQuest sphereQuest)
        {
            var res = false;
            Step = QuestComponentKind.Progress;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActObjSphere":
                            {
                                var template = act.GetTemplate<QuestActObjSphere>();
                                if (components[componentIndex].Id == sphereQuest.ComponentID)
                                {
                                    res = act.Use(Owner, this, 0);
                                    Status = QuestStatus.Ready;
                                    ComponentId = components[componentIndex].Id;
                                    //Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                    _log.Warn("[Quest] OnEnterSphere: do it - {0}, ComponentId {1}, Step {2}, Status {3}, act.DetailType {4}", TemplateId, ComponentId, Step, Status, act.DetailType);
                                }
                                break;
                            }
                        default:
                            // здесь еще есть компоненты, которые не проверили
                            _log.Warn("[Quest] OnEnterSphere: wants to do it - {0}, ComponentId {1}, Step {2}, Status {3}, act.DetailType {4}", TemplateId, ComponentId, Step, Status, act.DetailType);
                            break;
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
                            Objectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (Objectives[i] > template.Count) // TODO check to overtime
                            {
                                Objectives[i] = template.Count;
                            }

                            break;
                        }
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();
                            Objectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (Objectives[i] > template.Count) // TODO check to overtime
                            {
                                Objectives[i] = template.Count;
                            }

                            break;
                        }
                    case "QuestActObjItemGroupGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();
                            Objectives[i] = 0;
                            foreach (var itemId in QuestManager.Instance.GetGroupItems(template.ItemGroupId))
                            {
                                Objectives[i] += Owner.Inventory.GetItemsCount(itemId);
                            }

                            if (Objectives[i] > template.Count) // TODO check to overtime
                            {
                                Objectives[i] = template.Count;
                            }

                            break;
                        }
                }
                _log.Warn("[Quest] RecalcObjectives: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
            }

            Update(send);
        }

        public void ClearObjectives()
        {
            Objectives = new int[ObjectiveCount];
        }

        public int[] GetObjectives(QuestComponentKind step)
        {
            return Objectives;
        }

        #region Packets and Database

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(TemplateId);
            stream.Write((byte)Status);
            foreach (var objective in Objectives) // TODO do-while, count 5
            {
                stream.Write(objective);
            }

            stream.Write(false);          // isCheckSet
            stream.WriteBc((uint)ObjId);  // ObjId
            stream.Write(0u);             // type(id)
            stream.WriteBc((uint)ObjId);  // ObjId
            stream.WriteBc((uint)ObjId);  // ObjId
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
            for (var i = 0; i < 5; i++)
            {
                Objectives[i] = stream.ReadInt32();
            }

            Step = (QuestComponentKind)stream.ReadByte();
            Time = stream.ReadDateTime();
        }

        public byte[] WriteData()
        {
            var stream = new PacketStream();
            foreach (var objective in Objectives)
            {
                stream.Write(objective);
            }

            stream.Write((byte)Step);
            stream.Write(Time);
            return stream.GetBytes();
        }
        #endregion
    }
}

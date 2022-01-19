using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
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
            var componentId = 0u;
            for (Step = QuestComponentKind.None; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step >= QuestComponentKind.Ready)
                {
                    Status = QuestStatus.Ready;
                }

                var components = Template.GetComponents(Step);
                if (components.Length == 0)
                {
                    continue;
                }
                int c;
                for (c = 0; c <= components.Length - 1; c++)
                {
                    var acts = QuestManager.Instance.GetActs(components[c].Id);
                    for (var i = 0; i < acts.Length; i++)
                    {
                        switch (acts[i].DetailType)
                        {
                            case "QuestActObjItemGather":
                                {
                                    var template = acts[i].GetTemplate<QuestActObjItemGather>();
                                    // TODO: Check both inventory and warehouse
                                    Owner.Inventory.Bag.GetAllItemsByTemplate(template.Id, -1, out _, out var objectivesCounted);
                                    Objectives[i] = objectivesCounted;
                                    //Objectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                                    if (Objectives[i] > template.Count) // TODO check to overtime
                                    {
                                        Objectives[i] = template.Count;
                                    }

                                    _log.Warn("Quest: {0} {1} {2}", Step, res, acts[i].DetailType);//  for debuging
                                    res = acts[i].Use(Owner, this, Objectives[c]);
                                    componentId = components[c].Id;
                                    break;
                                }
                            case "QuestActConReportNpc":
                            case "QuestActConAutoComplete":
                            case "QuestActObjItemUse":
                                res = false;
                                break;
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                res = acts[i].Use(Owner, this, SupplyItem);
                                componentId = components[c].Id;
                                break;
                            default:
                                res = acts[i].Use(Owner, this, Objectives[c]);
                                componentId = components[c].Id;
                                break;
                        }

                        _log.Warn("Quest: {0} {1} {2}", Step, res, acts[i].DetailType); // for debuging
                    }
                }
                //componentId = components[c - 1].Id;
                if (!res)
                {
                    return componentId;
                }
            }
            return res ? componentId : 0;
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
                switch (components.Length)
                {
                    case 0 when Step == QuestComponentKind.Ready:
                        Owner.Quests.Complete(TemplateId, 0);
                        continue;
                    case 0:
                        continue;
                }
                int c;
                for (c = 0; c <= components.Length - 1; c++)
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
                                            case "QuestActObjItemGather" when questSupplyItem.ItemId == questItemGather.ItemId:
                                                res = acts[i].Use(Owner, this, SupplyItem);
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
                                res = false;
                                break;
                            case "QuestActObjSphere":
                                {
                                    // подготовим работу QuestSphere
                                    var template = acts[i].GetTemplate<QuestActObjSphere>();
                                    Status = QuestStatus.Progress;
                                    ComponentId = components[c].Id;

                                    var sphereQuestTrigger = new SphereQuestTrigger();
                                    sphereQuestTrigger.Sphere = SphereQuestManager.Instance.GetQuestSpheres(components[c].Id);
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
                                    _log.Warn("[Quest] DoCurrentStep:  character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, acts[i].DetailType);
                                    break;
                                }
                            default:
                                //case "QuestActObjItemGather":
                                //case "QuestActObjMonsterHunt":
                                //case "QuestActObjMonsterGroupHunt":
                                // эти акты всегда парные! ItemGather & MonsterHunt, ItemGather & MonsterGroupHunt
                                {
                                    //res = acts.Select(t => t.Use(Owner, this, Objectives[c])).ToList().Contains(true);
                                    res = acts[i].Use(Owner, this, Objectives[c]);
                                    // проверка результатов на валидность
                                    if (res)
                                    {
                                        // компонент - выполнен
                                        Status = QuestStatus.Ready;
                                        componentId = components[c].Id;
                                    }
                                    else
                                    {
                                        // компонент - в процессе выполнения
                                        Status = QuestStatus.Progress;
                                        componentId = 0;
                                        // если res == false, также надо слать пакет SCQuestContextUpdatedPacket
                                    }
                                    _log.Warn("[Quest] DoCurrentStep:  character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, acts[i].DetailType);
                                    break;
                                }
                        }
                        SupplyItem = 0;
                        _log.Warn("Quest: {0} {1} {2}", Step, res, acts[i].DetailType); // for debuging
                    }
                }
                if (!res)
                {
                    break;
                }
                //componentId = components[c - 1].Id;
            }
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, componentId));
        }

        public uint Complete(int selected)
        {
            var res = false;
            var componentId = 0u;
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
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    var selective = 0;
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActSupplySelectiveItem":
                                {
                                    selective++;
                                    if (selective == selected)
                                    {
                                        res = act.Use(Owner, this, Objectives[componentIndex]);
                                        componentId = components[componentIndex].Id;
                                        _log.Warn("Quest - {0}: ComponentId {1}, Step {2}, Status {3}, res {4}, act.DetailType {5}", TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                    }
                                    break;
                                }
                            case "QuestActSupplyItem":
                                res = act.Use(Owner, this, SupplyItem);
                                _log.Warn("Quest - {0}: ComponentId {1}, Step {2}, Status {3}, res {4}, act.DetailType {5}", TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                break;
                            case "QuestActConReportNpc":
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                componentId = components[componentIndex].Id;
                                _log.Warn("Quest - {0}: ComponentId {1}, Step {2}, Status {3}, res {4}, act.DetailType {5}", TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                break;
                            case "QuestActConAutoComplete":
                                res = true;
                                break;
                            default:
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                componentId = components[componentIndex].Id;
                                var CStep = Template.LetItDone;
                                if (CStep && res == false)
                                {
                                    EarlyCompletion = true;
                                    res = true;
                                }
                                _log.Warn("Quest - {0}: ComponentId {1}, Step {2}, Status {3}, res {4}, act.DetailType {5}", TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                break;
                        }
                        SupplyItem = 0;
                    }
                    if (!res)
                    {
                        break;
                    }
                }
                if (!res)
                    return componentId;
            }
            return res ? componentId : 0;
        }

        public uint Complete0(int selected)
        {
            var res = false;
            var componentId = 0u;
            Step = QuestComponentKind.Ready;
            for (; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step >= QuestComponentKind.Drop)
                {
                    Status = QuestStatus.Completed;
                }

                var component = Template.GetComponent(Step);
                if (component == null)
                {
                    continue;
                }

                if (Step >= QuestComponentKind.Reward)
                {
                    componentId = component.Id;
                }

                var acts = QuestManager.Instance.GetActs(component.Id);
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
                                    res = acts[i].Use(Owner, this, Objectives[i]);
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
                            res = acts[i].Use(Owner, this, Objectives[i]);
                            break;
                    }

                    SupplyItem = 0;
                }
                if (!res)
                {
                    break;
                }
            }
            return res ? componentId : 0;
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
                                    var interactionTarget = (Doodad)target;
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
                                List<Item> item;
                                Owner.Inventory.Bag.GetAllItemsByTemplate(template.ItemId, -1, out item, out Objectives[componentIndex]);
                                if (Objectives[componentIndex] > 0)
                                {
                                    res = true;
                                    //Objectives[componentIndex]++; // TODO проверить нужен он здесь?
                                }
                                break;
                            }
                        case "QuestActObjItemGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGather>();
                                Objectives[componentIndex] = Owner.Inventory.GetItemsCount(template.ItemId);
                                res = true;
                                break;
                            }
                        case "QuestActObjItemGroupUse":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupUse>();
                                List<Item> item;
                                Owner.Inventory.Bag.GetAllItemsByTemplate(template.ItemGroupId, -1, out item, out Objectives[componentIndex]);
                                if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item[0].TemplateId))
                                {
                                    res = true;
                                    //Objectives[componentIndex]++; // TODO проверить нужен он здесь?
                                }
                                break;
                            }
                    }
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

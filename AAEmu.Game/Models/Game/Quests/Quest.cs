using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
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
        private int NeedObjective { get; set; }
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
            NeedObjective = 0;

            var res = false;
            var component = GetCurrentComponent(); // в начале берем всегда первый компонент
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                switch (acts[i].DetailType)
                {
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();

                            NeedObjective = template.Count;

                            // TODO: Check both inventory and warehouse
                            Owner.Inventory.Bag.GetAllItemsByTemplate(template.Id, -1, out _, out var objectivesCounted);
                            CurrentObjectives[i] = objectivesCounted;
                            //CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                            {
                                CurrentObjectives[i] = template.Count;
                                Status = QuestStatus.Ready;
                            }

                            _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging
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
                    default:
                        res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                        break;
                }

                _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging

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
            var objective = 0;
            var component = GetCurrentComponent(); // возьмем компонент
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                switch (acts[i].DetailType)
                {
                    case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                        {
                            GetNextComponent(); // укажем на следующий компонент
                            _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging
                            var componentnext = GetCurrentComponent(); // возьмем компонент
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
                        res = false;
                        break;
                    default:
                        res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                        objective = CurrentObjectives[i];
                        break;
                }
                SupplyItem = 0;

                _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging

            }

            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, component.Id));

            if (NeedObjective != 0 && objective >= NeedObjective)
            {
                GetNextComponent(); // укажем на следующий компонент
                _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1}", component.KindId, res, Status, ComponentId);//  for debuging
            }
        }

        public uint Complete(int selected)
        {
            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
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

                _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging
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
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return 0;
            }

            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
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
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
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

        public void OnDoodadGather(uint objId, int selected)
        {
            //var doodad = WorldManager.Instance.GetDoodad(objId);
            //if (doodad == null)
            //    return;

            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                switch (acts[i].DetailType)
                {
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();

                            NeedObjective = template.Count;

                            Owner.Inventory.Bag.GetAllItemsByTemplate(template.Id, -1, out _, out var objectivesCounted);
                            CurrentObjectives[i] = objectivesCounted;
                            if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                            {
                                CurrentObjectives[i] = template.Count;
                                Status = QuestStatus.Ready;
                            }

                            _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging
                            res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                            break;
                        }
                    default:
                        //res = acts[i].Use(Owner, this, CurrentObjectives[i]);
                        break;
                }

                _log.Warn("Quest: componentId:{4} step:{0} status:{3} res:{1} DetailType:{2}", component.KindId, res, acts[i].DetailType, Status, ComponentId);//  for debuging

            }
            Update(res);
        }

        public void OnKill(Npc npc)
        {
            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];
                switch (act.DetailType)
                {
                    case "QuestActObjMonsterHunt":
                        {
                            var template = acts[i].GetTemplate<QuestActObjMonsterHunt>();

                            NeedObjective = template.Count;

                            if (template.NpcId == npc.TemplateId)
                            {
                                res = true;
                                CurrentObjectives[i]++;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                    case "QuestActObjMonsterGroupHunt":
                        {
                            var template = acts[i].GetTemplate<QuestActObjMonsterGroupHunt>();

                            NeedObjective = template.Count;

                            if (QuestManager.Instance.CheckGroupNpc(template.QuestMonsterGroupId, npc.TemplateId))
                            {
                                res = true;
                                CurrentObjectives[i]++;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                }
            }
            Update(res);
        }

        public void OnItemGather(Item item, int count)
        {
            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];
                switch (act.DetailType)
                {
                    case "QuestActSupplyItem":
                        {
                            var template = acts[i].GetTemplate<QuestActSupplyItem>();

                            NeedObjective = template.Count;

                            if (template.ItemId == item.TemplateId)
                            {
                                res = true;
                                SupplyItem += count;
                                if (SupplyItem >= template.Count) // TODO check to overtime
                                {
                                    SupplyItem = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();

                            NeedObjective = template.Count;

                            if (template.ItemId == item.TemplateId)
                            {
                                res = true;
                                CurrentObjectives[i] += count;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                    case "QuestActObjItemGroupGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();

                            NeedObjective = template.Count;

                            if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                            {
                                res = true;
                                CurrentObjectives[i] += count;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                }
            }

            Update(res);
        }

        public void OnItemUse(Item item)
        {
            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];
                switch (act.DetailType)
                {
                    case "QuestActObjItemUse":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemUse>();

                            NeedObjective = template.Count;

                            if (template.ItemId == item.TemplateId)
                            {
                                res = true;
                                CurrentObjectives[i]++;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                    case "QuestActObjItemGroupUse":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupUse>();

                            NeedObjective = template.Count;

                            if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                            {
                                res = true;
                                CurrentObjectives[i]++;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }

                            break;
                        }
                }
            }

            Update(res);
        }

        public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
        {
            var res = false;

            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            if (component != null)
            {
                var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
                for (var i = 0; i < acts.Length; i++)
                {
                    var act = acts[i];
                    if (act.DetailType == "QuestActObjInteraction")
                    {
                        var template = acts[i].GetTemplate<QuestActObjInteraction>();

                        NeedObjective = template.Count;

                        if (template.WorldInteractionId == type)
                        {
                            var interactionTarget = (Doodad)target;
                            if (template.DoodadId == interactionTarget.TemplateId)
                            {
                                ObjId = interactionTarget.ObjId;

                                res = true;
                                CurrentObjectives[i]++;
                                if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                {
                                    CurrentObjectives[i] = template.Count;
                                    Status = QuestStatus.Ready;
                                }
                            }
                        }
                    }
                    if (act.DetailType == "QuestActObjItemGather")
                    {
                        var template = acts[i].GetTemplate<QuestActObjItemGather>();

                        NeedObjective = template.Count;

                        var items = new List<Item>();
                        if (target != null)
                        {
                            var interactionTarget = (Doodad)target;
                            ObjId = interactionTarget.ObjId;
                        }

                        res = true;
                        CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                        if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                        {
                            CurrentObjectives[i] = template.Count;
                            Status = QuestStatus.Ready;
                        }
                    }

                    if (act.DetailType == "QuestActObjMonsterGroupHunt")
                    {
                        GetNextComponent(); // укажем на следующий компонент
                        _log.Warn("Quest: componentId:{0} step:{1} status:{2} res:{3}", ComponentId, component.KindId, Status, res);//  for debuging
                        var componentnext = GetCurrentComponent(); // возьмем компонент
                        if (componentnext == null) break;

                        var actsnext = QuestManager.Instance.GetActs(componentnext.Id);
                        foreach (var qa in actsnext)
                        {
                            switch (qa.DetailType)
                            {
                                case "QuestActObjItemGather":
                                    var template = qa.GetTemplate<QuestActObjItemGather>();
                                    NeedObjective = template.Count;
                                    if (target != null)
                                    {
                                        var interactionTarget = (Doodad)target;
                                        ObjId = interactionTarget.ObjId;
                                    }
                                    res = true;
                                    CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                                    if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                                    {
                                        CurrentObjectives[i] = template.Count;
                                        Status = QuestStatus.Ready;
                                    }
                                    break;
                                default:
                                    res = false;
                                    break;
                            }
                            GetPrevComponent(); // укажем на предыдущий компонент
                            _log.Warn("Quest: componentId:{0} step:{1} status:{2} res:{3}", ComponentId, component.KindId, Status, res);//  for debuging
                            break;
                        }
                    }
                }
            }

            //if (!res)
            //{
            //    Owner.SendPacket(new SCQuestContextUpdatedPacket(this, component.Id));
            //}
            Update(res);
        }

        public void OnLevelUp()
        {
            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
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

            Update(res);
        }

        public void OnQuestComplete(uint questContextId)
        {
            var res = false;
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
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

            Update(res);
        }

        public void RecalcObjectives(bool send = true)
        {
            var component = GetCurrentComponent(); // возьмем компонент
            if (component == null)
            {
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id); // обработаем все акты для текущего компонента
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];

                switch (act.DetailType)
                {
                    case "QuestActSupplyItem":
                        {
                            var template = acts[i].GetTemplate<QuestActSupplyItem>();

                            NeedObjective = template.Count;

                            CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                            {
                                //CurrentObjectives[i] = template.Count;
                                GetNextComponent();
                            }

                            break;
                        }
                    case "QuestActObjItemGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGather>();

                            NeedObjective = template.Count;

                            CurrentObjectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                            {
                                //CurrentObjectives[i] = template.Count;
                                GetNextComponent();
                            }

                            break;
                        }
                    case "QuestActObjItemGroupGather":
                        {
                            var template = acts[i].GetTemplate<QuestActObjItemGroupGather>();

                            NeedObjective = template.Count;

                            CurrentObjectives[i] = 0;
                            foreach (var itemId in QuestManager.Instance.GetGroupItems(template.ItemGroupId))
                            {
                                CurrentObjectives[i] += Owner.Inventory.GetItemsCount(itemId);
                            }

                            if (CurrentObjectives[i] >= template.Count) // TODO check to overtime
                            {
                                //CurrentObjectives[i] = template.Count;
                                GetNextComponent();
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

        public void CheckThatTheNextStepIsReady(int index)
        {
            // проверим следующий компонент на QuestComponentKind.Ready
            if (index >= Template.Components.Count - 1)
            {
                index = Template.Components.Count - 2; // укажем на последний компонент
            }
            var (_, component) = Template.Components.ElementAt(index + 1); // возьмём компонент следующий за текущим
            Status = component.KindId == QuestComponentKind.Ready ? QuestStatus.Ready : QuestStatus.Progress;

            return;
        }

        public void GetFirstComponent()
        {
            var (key, component) = Template.Components.ElementAtOrDefault(0);
            Step = component.KindId; // указываем на первый шаг
            ComponentId = key;
        }

        public void GetNextComponent()
        {
            // может быть несколько компонентов с одинаковым шагом Step
            for (var i = 0; i < Template.Components.Count; i++)
            {
                var (key, component) = Template.Components.ElementAt(i);
                if (key != ComponentId)
                    continue;

                if (i == Template.Components.Count - 1) // вернем последний компонет
                {
                    Step = component.KindId;
                    ComponentId = key;
                    CheckThatTheNextStepIsReady(i + 1); // проверим следующий компонент на QuestComponentKind.Ready
                    return;
                }

                (key, component) = Template.Components.ElementAt(i + 1); // находим компонент следующий за текущим
                Step = component.KindId;
                ComponentId = key;
                CheckThatTheNextStepIsReady(i + 1); // проверим следующий компонент на QuestComponentKind.Ready
                return;
            }
        }

        public void GetPrevComponent()
        {
            // может быть несколько компонентов с одинаковым шагом Step
            for (var i = 0; i < Template.Components.Count; i++)
            {
                var (key, component) = Template.Components.ElementAt(i);
                if (key != ComponentId)
                    continue;

                if (i == Template.Components.Count - 1) // вернем последний компонет
                {
                    Step = component.KindId;
                    ComponentId = key;
                    CheckThatTheNextStepIsReady(i + 1); // проверим следующий компонент на QuestComponentKind.Ready
                    return;
                }

                (key, component) = Template.Components.ElementAt(i - 1); // находим компонент предыдущий за текущим
                Step = component.KindId;
                ComponentId = key;
                CheckThatTheNextStepIsReady(i - 1); // проверим следующий компонент на QuestComponentKind.Ready
                return;
            }
        }

        public QuestComponent GetCurrentComponent()
        {
            // может быть несколько компонентов с одинаковым шагом Step
            for (var i = 0; i < Template.Components.Count; i++)
            {
                var (key, component) = Template.Components.ElementAt(i);
                if (key != ComponentId)
                    continue;

                (key, component) = Template.Components.ElementAt(i); // находим компонент следующий за текущим
                Step = component.KindId;
                ComponentId = key;
                CheckThatTheNextStepIsReady(i); // проверим следующий компонент на QuestComponentKind.Ready
                return component;
            }

            return null;
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

            ComponentId = stream.ReadUInt32();
            Step = (QuestComponentKind)stream.ReadByte();
            Time = stream.ReadDateTime();
        }

        public byte[] WriteData()
        {
            var stream = new PacketStream();

            for (var i = QuestComponentKind.None; i <= QuestComponentKind.Reward; i++)
                foreach (var value in ObjectivesForStep[i])
                    stream.Write(value);

            stream.Write(ComponentId);
            stream.Write((byte)Step);
            stream.Write(Time);
            return stream.GetBytes();
        }
    }
}

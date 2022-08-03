using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Quests;

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
        public int LeftTime => Time > DateTime.UtcNow ? (int)(Time - DateTime.UtcNow).TotalMilliseconds : -1;
        public int SupplyItem { get; set; }
        public bool EarlyCompletion { get; set; }
        public bool ExtraCompletion { get; set; }
        public int OverCompletionPercent { get; set; }
        public long DoodadId { get; set; }
        public long ObjId { get; set; }
        public uint ComponentId { get; set; }
        public QuestAcceptorType QuestAcceptorType { get; set; }
        public uint AcceptorType { get; set; }
        public QuestCompleteTask QuestTask { get; set; }
        public List<ItemCreationDefinition> QuestActItemsPool { get; set; }
        public Dictionary<ShopCurrencyType,int> QuestActCoinsPool { get; set; }

        public uint GetActiveComponent()
        {
            return Template.GetComponent(Step).Id;
        }

        public Quest()
        {
            Objectives = new int[ObjectiveCount];
            SupplyItem = 0;
            EarlyCompletion = false;
            ExtraCompletion = false;
            ObjId = 0;
            QuestActItemsPool = new List<ItemCreationDefinition>();
            QuestActCoinsPool = new Dictionary<ShopCurrencyType, int>();
        }

        public Quest(QuestTemplate template)
        {
            TemplateId = template.Id;
            Template = template;
            Objectives = new int[ObjectiveCount];
            SupplyItem = 0;
            EarlyCompletion = false;
            ExtraCompletion = false;
            ObjId = 0;
            QuestActItemsPool = new List<ItemCreationDefinition>();
            QuestActCoinsPool = new Dictionary<ShopCurrencyType, int>();
        }

        private void CheckStatus()
        {
            // проверим следующий компонент на QuestComponentKind.Ready (Check the following component for QuestComponentKind.Ready)
            var (_, component) = Template.Components.ElementAt(1); // возьмём компонент следующий за Start or None (let's take the component following Start or None)
            if (component.KindId == QuestComponentKind.Supply)
            {
                var (_, component2) = Template.Components.ElementAt(2); // возьмём компонент следующий за Supply (let's take the component following Supply)
                // TODO added for quest Id=748 - получение того же предмета повторно (obtaining the same subject again)
                var acts2 = QuestManager.Instance.GetActs(component2.Id);
                if (component2.KindId == QuestComponentKind.Progress && acts2.Any(qa => qa.DetailType == "QuestActSupplyItem"))
                {
                    Status = QuestStatus.Ready;
                    return;
                }
                Status = component2.KindId == QuestComponentKind.Progress ? QuestStatus.Progress : QuestStatus.Ready;
                return;
            }
            var acts = QuestManager.Instance.GetActs(component.Id);
            if (acts.Length == 0)
            {
                Status = QuestStatus.Ready;
            }
            else
            {
                Status = component.KindId == QuestComponentKind.Progress ? QuestStatus.Progress : QuestStatus.Ready;
            }
        }

        public bool Start()
        {
            var res = false;
            var supply = false;
            var acceptNpc = false;
            for (Step = QuestComponentKind.None; Step < QuestComponentKind.Fail; Step++)
            {
                var components = Template.GetComponents(Step);
                if (components.Length == 0 || Step is QuestComponentKind.Fail or QuestComponentKind.Drop)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);

                    var questActConAcceptNpc = acts.All(a => a.DetailType == "QuestActConAcceptNpc");
                    if (acts.Length > 0 && questActConAcceptNpc)
                    {
                        // оказывается может быть несколько Npc с которыми можно заключить квест! (It turns out that there may be several NPCs with which you can make a quest!)
                        var accept = acts.Select(t => t.Use(Owner, this, Objectives[componentIndex])).ToList();
                        if (accept.Contains(true))
                        {
                            res = true;
                            acceptNpc = true;
                            ComponentId = components[componentIndex].Id;
                            CheckStatus();
                            _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
                        }
                        else
                        {
                            _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
                            return false; // не тот Npc, что нужен по квесту, выход (Not the NPC that is needed by the quest, the exit)
                        }
                        UseSkill(components, componentIndex);
                    }

                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            default:
                                //case "QuestActObjTalk":
                                //case "QuestActObjTalkNpcGroup":
                                supply = true; // прерываем цикл и на метод Update() не переходим (interrupt the cycle and do not go to the update() method)
                                _log.Warn($"[Quest] Start: character {Owner.Name}, default do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                // TODO added for quest Id=4402
                                goto exit;
                            case "QuestActConAcceptDoodad": // старт ежедневного квеста (The start of the daily quest)
                            case "QuestActConAcceptNpcKill":
                            case "QuestActConAcceptNpc":
                                {
                                    if (acceptNpc)
                                    {
                                        // мы уже проверяли этот пункт, поэтому пропускаем (We have already checked this item, so we miss)
                                        break;
                                    }
                                    res = act.Use(Owner, this, Objectives[componentIndex]);
                                    if (res)
                                    {
                                        ComponentId = components[componentIndex].Id;
                                        CheckStatus();
                                        _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    }
                                    else
                                    {
                                        _log.Warn($"[Quest] Start failed: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                        return false; // не тот Npc, что нужен по квесту, выход (Not the NPC that is needed by the quest, the exit)
                                    }
                                    UseSkill(components, componentIndex);
                                    break;
                                }
                            case "QuestActSupplyItem":
                                {
                                    res = act.Use(Owner, this, SupplyItem); // if SupplyItem = 0, we get the item
                                    if (!res)
                                    {
                                        Owner.SendErrorMessage(ErrorMessageType.BagFull);
                                    }
                                    Step = QuestComponentKind.Supply; // в процессе работы метода ItemGather  переключается на Progress (in the process of ItemGather method operation switches to Progress)
                                    supply = res; // если было пополнение предметом, то на метод Update() не переходить (If there was a replenishment of an object, then do not go to the Update() method)
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActCheckTimer":
                                {
                                    // TODO настройка и старт таймера ограничения времени на квест (Timer - setting and starting time limit for the quest)
                                    var template = act.GetTemplate<QuestActCheckTimer>();
                                    res = act.Use(Owner, this, template.LimitTime);
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActObjSphere":
                                {
                                    // только для сфер (for spheres only)
                                    Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    Update();
                                    break;
                                }
                        }
                    }
                }
            }
            exit:
            Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));

            if (Status == QuestStatus.Progress && !supply)
            {
                Step = QuestComponentKind.Progress; // потому, что уже стоял на Fail (Because he was already standing on Fail)
                Update(res);
            }

            return res;
        }

        /// <summary>
        /// Метод предназначен для вызова из скрипта QuestCmd, команда /quest add <questId> (The method is used to call from the QuestCmd script, the command /quest add <questId>)
        /// </summary>
        /// <returns></returns>
        public void StartFirstOnly()
        {
            var res = false;
            for (Step = QuestComponentKind.None; Step < QuestComponentKind.Fail; Step++)
            {
                var components = Template.GetComponents(Step);
                if (components.Length == 0 || Step == QuestComponentKind.Fail || Step == QuestComponentKind.Drop)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            default:
                                _log.Warn($"[Quest] Start: character {Owner.Name}, default don't do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            case "QuestActConAcceptDoodad": // старт ежедневного квеста (start of the daily quest)
                            case "QuestActConAcceptNpcKill":
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                if (res)
                                {
                                    ComponentId = components[componentIndex].Id;
                                    CheckStatus();
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                }
                                else
                                {
                                    _log.Warn($"[Quest] Start failed: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    return; // не тот Npc, что нужен по квесту, выход (not the Npc that is needed on the quest, exit)
                                }
                                UseSkill(components, componentIndex);
                                break;
                            case "QuestActConAcceptNpc":
                                {
                                    // не проверяем Npc при взятии квеста (do not check the Npc when taking the quest)
                                    act.Use(Owner, this, 0);
                                    ComponentId = components[componentIndex].Id;
                                    CheckStatus();
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    UseSkill(components, componentIndex);
                                    break;
                                }
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    res = act.Use(Owner, this, 0); // получим предмет (get the item)
                                    if (!res)
                                    {
                                        Owner.SendErrorMessage(ErrorMessageType.BagFull);
                                    }
                                    Step = QuestComponentKind.Supply; // в процессе работы метода ItemGather  переключается на Progress (in the process of ItemGather method operation switches to Progress)
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActCheckTimer":
                                {
                                    // TODO настройка и старт таймера ограничения времени на квест (Timer - setting and starting time limit for the quest)
                                    var template = act.GetTemplate<QuestActCheckTimer>();
                                    res = act.Use(Owner, this, template.LimitTime);
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActObjSphere":
                                {
                                    // только для сфер (for spheres only)
                                    Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));
                                    _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    Update();
                                    break;
                                }
                        }
                        // _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[i].DetailType}");
                    }
                }
            }
            Step = QuestComponentKind.Progress; // потому, что уже стоял на Fail (Because he was already standing on Fail)
            Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));
        }

        public void Update(bool send = true)
        {
            if (!send) { return; }

            for (; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step is QuestComponentKind.Fail or QuestComponentKind.Drop)
                    continue;

                if (Step >= QuestComponentKind.Drop)
                    Status = QuestStatus.Completed;
                else if (Step >= QuestComponentKind.Ready)
                    Status = QuestStatus.Ready;

                var components = Template.GetComponents(Step);
                switch (components.Length)
                {
                    case 0 when Step == QuestComponentKind.Ready: // если нет шага Ready переходим к завершению квеста (if there is no Ready step, go to the end of the quest)
                        {
                            // делаем задержку 6 сек перед вызовом Owner.Quests.Complete(TemplateId, 0); (delay 6 sec before calling Owner.Quests.Complete(TemplateId, 0);)
                            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                            var delay = 6;
                            QuestTask = new QuestCompleteTask(Owner, TemplateId);
                            TaskManager.Instance.Schedule(QuestTask, TimeSpan.FromSeconds(delay));
                            return;
                        }
                    case 0: // пропустим пустые шаги (let's skip the empty steps)
                        continue;
                }

                var completes = new List<bool>();
                for (var i = 0; i < components.Length; i++)
                {
                    completes.Add(false);
                }
                EarlyCompletion = false;
                ExtraCompletion = false;
                var selectiveList = new List<bool>();
                var selective = false;
                var complete = false;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    if (Step == QuestComponentKind.Progress)
                        ComponentId = components[componentIndex].Id;

                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    complete = act.Use(Owner, this, SupplyItem);
                                    if (!complete)
                                    {
                                        Owner.SendErrorMessage(ErrorMessageType.BagFull);
                                    }
                                    _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    var next = QuestComponentKind.Progress;
                                    var componentnext = Template.GetComponent(next);
                                    if (componentnext == null)
                                        break;
                                    var actsnext = QuestManager.Instance.GetActs(componentnext.Id);
                                    foreach (var qa in actsnext)
                                    {
                                        var questSupplyItem = (QuestActSupplyItem)QuestManager.Instance.GetActTemplate(act.DetailId, "QuestActSupplyItem");
                                        var questItemGather = (QuestActObjItemGather)QuestManager.Instance.GetActTemplate(qa.DetailId, "QuestActObjItemGather");
                                        switch (qa.DetailType)
                                        {
                                            case "QuestActObjItemGather" when questSupplyItem.ItemId == questItemGather.ItemId:
                                                Owner.Inventory.Bag.GetAllItemsByTemplate(questSupplyItem.ItemId, -1, out _, out Objectives[componentIndex]);
                                                complete = qa.Use(Owner, this, Objectives[componentIndex]);
                                                Step = next;
                                                ComponentId = components[componentIndex].Id;
                                                break;
                                            default:
                                                complete = false;
                                                break;
                                        }
                                        _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    }
                                    break;
                                }
                            case "QuestActConReportJournal":
                            case "QuestActConReportDoodad":
                            case "QuestActConReportNpc":
                                complete = act.Use(Owner, this, Objectives[componentIndex]);
                                // проверка результатов на валидность (Validation of results)
                                if (complete)
                                {
                                    // компонент - выполнен, мы у нужного Npc (component - done, we're at the right Npc)
                                    Status = QuestStatus.Ready;
                                    _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                    Owner.Quests.Complete(TemplateId, 0);
                                    return;
                                }
                                // компонент - выполнен, мы у нужного Npc (component - done, we're at the right Npc)
                                Status = QuestStatus.Ready;
                                //_log.Warn("[Quest] Update: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, complete {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, complete, act.DetailType);
                                break;
                            case "QuestActConAutoComplete":
                                // компонент - выполнен (component - ready)
                                complete = true;
                                Status = QuestStatus.Ready;
                                _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                Owner.Quests.Complete(TemplateId, 0);
                                return;
                            case "QuestActObjSphere":
                                {
                                    // подготовим работу QuestSphere (prepare QuestSphere's work)
                                    //var template = act.GetTemplate<QuestActObjSphere>();
                                    Status = QuestStatus.Progress;
                                    ComponentId = components[componentIndex].Id;

                                    foreach (var sphere in SphereQuestManager.Instance.GetQuestSpheres(components[componentIndex].Id))
                                    {
                                        var sphereQuestTrigger = new SphereQuestTrigger();
                                        sphereQuestTrigger.Sphere = sphere;

                                        if (sphereQuestTrigger.Sphere == null)
                                        {
                                            _log.Warn($"[Quest] QuestActObjSphere: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                            _log.Warn($"[Quest] QuestActObjSphere: Sphere not found with cquest {components[componentIndex].Id} in quest_sign_spheres.json!");
                                            return;
                                        }

                                        sphereQuestTrigger.Owner = Owner;
                                        sphereQuestTrigger.Quest = this;
                                        sphereQuestTrigger.TickRate = 500;

                                        SphereQuestManager.Instance.AddSphereQuestTrigger(sphereQuestTrigger);
                                    }

                                    const int Duration = 500;
                                    // TODO : Add a proper delay in here
                                    Task.Run(async () =>
                                    {
                                        await Task.Delay(Duration);
                                    });
                                    _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    return;
                                }
                            case "QuestActEtcItemObtain":
                                {
                                    // TODO: added for quest Id=882.
                                    // ничего не делаем (We're not doing anything)
                                    //_log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {completes[componentIndex]}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActObjItemGather":
                                {
                                    // нужно посмотреть в инвентарь, так как после Start() ещё не знаем, есть предмет в инвентаре или нет (we need to look in the inventory, because after Start() we don't know yet if the item is in the inventory or not)
                                    var template = act.GetTemplate<QuestActObjItemGather>();
                                    if (Objectives[componentIndex] == 0)
                                        Objectives[componentIndex] = Owner.Inventory.GetItemsCount(template.ItemId);
                                    complete = act.Use(Owner, this, Objectives[componentIndex]);
                                    completes[componentIndex] = complete; // продублируем информацию (let's duplicate the information)
                                    // проверка результатов на валидность (Validation of results)
                                    if (Template.Selective)
                                    {
                                        // Если Selective = true, то разрешается быть подходящим одному предмету из нескольких (If Selective = true, it is allowed to be matched to one item out of several)
                                        selectiveList.Add(completes[componentIndex]);
                                        selective = true;
                                    }
                                    Status = complete ? QuestStatus.Ready : QuestStatus.Progress;
                                    // если complete == false, также надо слать пакет SCQuestContextUpdatedPacket (if complete == false, we should also send SCQuestContextUpdatedPacket)
                                    break;
                                }
                            default:
                                //case "QuestActObjItemUse":
                                //case "QuestActObjMonsterHunt":
                                //case "QuestActObjMonsterGroupHunt":
                                // эти акты могут быть парными: ItemGather & MonsterHunt & MonsterGroupHunt & Interaction
                                {
                                    complete = act.Use(Owner, this, Objectives[componentIndex]);
                                    completes[componentIndex] = complete; // продублируем информацию
                                    // проверка результатов на валидность (Validation of results)
                                    if (Template.Selective)
                                    {
                                        // Если Selective = true, то разрешается быть подходящим одному предмету из нескольких
                                        selectiveList.Add(completes[componentIndex]);
                                        selective = true;
                                    }
                                    Status = complete ? QuestStatus.Ready : QuestStatus.Progress;
                                    // если complete == false, также надо слать пакет SCQuestContextUpdatedPacket (if complete == false, we should also send SCQuestContextUpdatedPacket)
                                    break;
                                }
                        }
                        SupplyItem = 0;
                        _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                    }

                    if (completes[componentIndex] || complete)
                    {
                        UseSkill(components, componentIndex);
                    }
                }

                // TODO added for quest Id=1135 - достаточно выполнение одного элемента itemGather || monsterHunt (it is enough to execute one element itemGather || monsterHunt)
                // TODO added for quest Id=1511 - обязательно выполнение обеих элементов itemGather (it is obligatory to perform both itemGather elements)
                if (components.Length > 1)
                {
                    // TODO added for quest id=4294 - нужен только itemGather, а ItemUse не нужен (only itemGather is needed, and ItemUse is not needed)
                    complete = Template.Score > 0 ? completes.Any(b => b) : completes.All(b => b);
                    Status = complete ? QuestStatus.Ready : QuestStatus.Progress;
                }
                if (selective)
                {
                    // Если Selective = true, то разрешается быть подходящим одному предмету из нескольких (If Selective = true, it is allowed to be matched to one item out of several)
                    if (selectiveList.Contains(true))
                    {
                        Status = QuestStatus.Ready;
                        complete = true;
                    }
                }

                if (complete && (EarlyCompletion || ExtraCompletion))
                {
                    break; // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест (the quest can be passed, but we do not let it end when it reaches 100% until we come to the Npc to pass the quest)
                }
                if (!complete)
                {
                    break;
                }
            }
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
        }

        /// <summary>
        /// Use Skill на себя или на Npc, с которым взаимодействуем (Use Skill on yourself or on the Npc you interact with)
        /// </summary>
        /// <param name="components"></param>
        /// <param name="componentIndex"></param>
        private void UseSkill(QuestComponent[] components, int componentIndex)
        {
            if (components.Length == 0) { return; }
            if (components[componentIndex].SkillId > 0)
            {
                if (components[componentIndex].SkillSelf)
                {
                    Owner.UseSkill(components[componentIndex].SkillId, Owner);
                }
                else
                {
                    if (components[componentIndex].NpcId > 0)
                    {
                        if (Owner.CurrentTarget is Npc npc)
                        {
                            if (npc.TemplateId == components[componentIndex].NpcId)
                            {
                                Owner.UseSkill(components[componentIndex].SkillId, npc);
                            }
                        }
                    }
                }
            }
            if (components[componentIndex].BuffId > 0)
            {
                Owner.Buffs.AddBuff(new Buff(Owner, Owner, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(components[componentIndex].BuffId), null, DateTime.UtcNow));
            }
        }

        private void UseSkill(QuestComponent component)
        {
            if (component == null) 
                return;
            if (component.SkillId > 0)
            {
                if (component.SkillSelf)
                {
                    Owner.UseSkill(component.SkillId, Owner);
                }
                else
                {
                    if (component.NpcId > 0)
                    {
                        if (Owner.CurrentTarget is Npc npc)
                        {
                            if (npc.TemplateId == component.NpcId)
                            {
                                Owner.UseSkill(component.SkillId, npc);
                            }
                        }
                    }
                }
            }
            if (component.BuffId > 0)
            {
                Owner.Buffs.AddBuff(new Buff(Owner, Owner, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(component.BuffId), null, DateTime.UtcNow));
            }
        }

        public void DistributeRewards()
        {
            // Distribute Items if needed
            if ((QuestActItemsPool.Count > 0) || (QuestActCoinsPool.Count > 0))
            {
                // TODO: Add a way to distribute honor or vocation badges in mail as well 

                if (Owner.Inventory.Bag.FreeSlotCount < QuestActItemsPool.Count)
                {
                    var mails = MailManager.Instance.CreateQuestRewardMails(Owner, this, QuestActItemsPool, QuestActCoinsPool);
                    foreach (var mail in mails)
                        if (!mail.Send())
                            Owner.SendErrorMessage(ErrorMessageType.MailUnknownFailure);

                    Owner.SendPacket(new SCQuestRewardedByMailPacket(new uint[] { TemplateId }));
                }
                else
                {
                    foreach (var item in QuestActItemsPool)
                    {
                        if (ItemManager.Instance.IsAutoEquipTradePack(item.TemplateId))
                        {
                            Owner.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, item.TemplateId, item.Count, item.GradeId);
                        }
                        else
                        {
                            Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, item.TemplateId, item.Count, item.GradeId);    
                        }
                    }
                }
            }

            foreach (var (currency, value) in QuestActCoinsPool)
            {
                if (value == 0)
                    continue;
                switch (currency)
                {
                    case ShopCurrencyType.Money:
                        Owner.ChangeMoney(SlotType.None, SlotType.Inventory, value);
                        break;
                    case ShopCurrencyType.Honor:
                        Owner.ChangeGamePoints(GamePointKind.Honor, value);
                        break;
                    case ShopCurrencyType.VocationBadges:
                        Owner.ChangeGamePoints(GamePointKind.Vocation, value);
                        break;
                    // case ShopCurrencyType.SiegeShop:
                    default:
                        // TODO: What currency is this actually ?
                        break;
                }
            }
        }

        public uint Complete(int selected)
        {
            var res = false;
            var step = QuestComponentKind.Ready; // покажем, что заканчиваем квест (let's show you that we're finishing the quest)
            for (; step <= QuestComponentKind.Reward; step++)
            {
                if (step >= QuestComponentKind.Drop)
                    Status = QuestStatus.Completed;

                var components = Template.GetComponents(step);
                if (components.Length == 0)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    if (step == QuestComponentKind.Ready)
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
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            case "QuestActSupplySelectiveItem":
                                {
                                    selective++;
                                    if (selective == selected)
                                    {
                                        res = act.Use(Owner, this, Objectives[componentIndex]);
                                        if (ComponentId == 0)
                                            ComponentId = components[componentIndex].Id;
                                        _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    }
                                    break;
                                }
                            case "QuestActSupplyItem":
                                res = act.Use(Owner, this, SupplyItem);
                                if (!res)
                                {
                                    Owner.SendErrorMessage(ErrorMessageType.BagFull);
                                }
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            case "QuestActConAutoComplete":
                                res = true;
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            default:
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                if (ComponentId == 0)
                                    ComponentId = components[componentIndex].Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                        }
                        SupplyItem = 0;
                        // _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
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

        public void AddCurrencyToQuestActCoinsPool(ShopCurrencyType shopCurrencyType, int amount)
        {
            if (QuestActCoinsPool.TryGetValue(shopCurrencyType, out var oldValue))
                QuestActCoinsPool.Remove(shopCurrencyType);
            amount += oldValue;
            QuestActCoinsPool.Add(shopCurrencyType, amount);
        }

        public int GetCustomExp() { return GetCustomSupplies("exp"); }

        public int GetCustomCopper() { return GetCustomSupplies("copper"); }

        private int GetCustomSupplies(string supply)
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
                            _log.Warn("[Quest] GetCustomSupplies Exp: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
                            break;
                        }
                    case "QuestActSupplyCoppers" when supply == "copper":
                        {
                            var template = act.GetTemplate<QuestActSupplyCopper>();
                            value = template.Amount;
                            _log.Warn("[Quest] GetCustomSupplies Coppers: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
                            break;
                        }
                    default:
                        value = 0;
                        _log.Warn("[Quest] GetCustomSupplies: character {0}, wants to do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
                        break;
                }
                //_log.Warn("[Quest] GetCustomSupplies: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType);
            }
            return value;
        }

        private void RemoveQuestItems()
        {
            for (var step = QuestComponentKind.None; step <= QuestComponentKind.Reward; step++)
            {
                var components = Template.GetComponents(step);
                if (components.Length == 0)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = QuestManager.Instance.GetActs(components[componentIndex].Id);
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActSupplyItem" when step == QuestComponentKind.Supply:
                                {
                                    var template = act.GetTemplate<QuestActSupplyItem>();
                                    if (template.DestroyWhenDrop && Owner.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlotType.Backpack)?.TemplateId == template.ItemId)
                                    {
                                        Owner.Inventory.TakeoffBackpack(ItemTaskType.QuestRemoveSupplies);
                                    }
                                    //Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, template.Count, null);
                                    Objectives[componentIndex] = Owner.Inventory.GetItemsCount(template.ItemId);
                                    Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, Objectives[componentIndex], null);
                                    //items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));
                                    _log.Warn("[Quest] RemoveQuestItems: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}, ItemId {6}, Count {7}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType, template.ItemId, template.Count);
                                    break;
                                }
                            case "QuestActObjItemGather":
                                {
                                    var template = act.GetTemplate<QuestActObjItemGather>();
                                    if (template.DestroyWhenDrop)
                                    {
                                        //Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, template.Count, null);
                                        Objectives[componentIndex] = Owner.Inventory.GetItemsCount(template.ItemId);
                                        Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, Objectives[componentIndex], null);
                                        //items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));
                                        _log.Warn("[Quest] RemoveQuestItems: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}, ItemId {6}, Count {7}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType, template.ItemId, template.Count);
                                    }
                                    break;
                                }
                            case "QuestActObjItemUse":
                                {
                                    var template = act.GetTemplate<QuestActObjItemUse>();
                                    if (template.DropWhenDestroy)
                                    {
                                        //Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, template.Count, null);
                                        Objectives[componentIndex] = Owner.Inventory.GetItemsCount(template.ItemId);
                                        Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, Objectives[componentIndex], null);
                                        //items.AddRange(Owner.Inventory.RemoveItem(template.ItemId, template.Count));
                                        _log.Warn("[Quest] RemoveQuestItems: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}, ItemId {6}, Count {7}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType, template.ItemId, template.Count);
                                    }
                                    break;
                                }
                        }
                    }
                }
            }
        }

        public void Drop(bool update)
        {
            Status = QuestStatus.Dropped;
            Step = QuestComponentKind.Drop;

            var component = Template.GetComponent(Step);

            UseSkill(component);

            if (update)
                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));

            RemoveQuestItems();
            for (var i = 0; i < ObjectiveCount; i++)
                Objectives[i] = 0;
        }

        public void OnReportToNpc(Npc npc, int selected)
        {
            var checking = false;
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
                                checking = act.Use(Owner, this, Objectives[componentIndex]);
                                // проверка результатов на валидность (Validation of results)
                                if (checking)
                                {
                                    // компонент - выполнен (component - ready)
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
                                    checking = act.Use(Owner, this, Objectives[componentIndex]);
                                    if (ComponentId == 0)
                                        ComponentId = components[componentIndex].Id;
                                }
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnReportToNpc: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                }
            }
            Update(checking);
        }

        public void OnReportToDoodad(Doodad doodad)
        {
            var checking = false;
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
                                    checking = true;
                                    Objectives[componentIndex]++;
                                }
                                break;
                            }
                    }
                    _log.Warn("[Quest] OnReportToDoodad: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                }
            }
            Update(checking);
        }

        public void OnTalkMade(Npc npc)
        {
            var checking = false;
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
                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn("[Quest] OnTalkMade: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                    }
                }
            }
            Update(checking);
        }

        public void OnKill(Npc npc)
        {
            var checking = false;
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
                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn("[Quest] OnKill: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                        case "QuestActObjMonsterGroupHunt":
                            {
                                var template = act.GetTemplate<QuestActObjMonsterGroupHunt>();
                                if (QuestManager.Instance.CheckGroupNpc(template.QuestMonsterGroupId, npc.TemplateId))
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn("[Quest] OnKill: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                    }
                }
            }
            Update(checking);
        }

        public void OnItemGather(Item item, int count)
        {
            var checking = false;
            var tmpStep = Step;
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
                                    checking = true;
                                    SupplyItem += count; // the same as Objectives, but for QuestActSupplyItem
                                    _log.Warn("[Quest] OnItemGather: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                    if (tmpStep == QuestComponentKind.Supply)
                                    {
                                        Step = tmpStep;
                                        return; // возврат в метод Start() (return to Start() method)
                                    }
                                }
                                break;
                            }
                        case "QuestActObjItemGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGather>();
                                if (template.ItemId == item.TemplateId)
                                {
                                    checking = true;
                                    Objectives[componentIndex] += count;
                                    _log.Warn("[Quest] OnItemGather: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                        case "QuestActObjItemGroupGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupGather>();
                                if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    checking = true;
                                    Objectives[componentIndex] += count;
                                    _log.Warn("[Quest] OnItemGather: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                        // TODO added for quest Id=4402
                        // TODO added for quest Id=266
                        default:
                            goto exit;
                    }
                }
                exit:;
            }
            Update(checking);
        }

        public void OnItemUse(Item item)
        {
            var checking = false;
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
                                // нужно посмотреть в инвентарь, есть предмет в инвентаре или нет (you need to look in your inventory to see if the item is in your inventory or not)
                                var template = act.GetTemplate<QuestActObjItemUse>();
                                if (template.ItemId == item.TemplateId)
                                {
                                    if (Objectives[componentIndex] < template.Count)
                                    {
                                        checking = true;
                                        Objectives[componentIndex]++;
                                    }
                                    else
                                    {
                                        checking = false; // cancel the rerun of the quest check Id=97
                                    }
                                    _log.Warn("[Quest] OnItemUse: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                        case "QuestActObjItemGroupUse":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupUse>();
                                if (QuestManager.Instance.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn("[Quest] OnItemUse: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                    }
                }
            }
            Update(checking);
        }

        public void OnInteraction(WorldInteractionType type, Units.BaseUnit target)
        {
            var checking = false;
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
                                //if (template.WorldInteractionId == type) // TODO This operator is commented out for the quest id=3708
                                {
                                    if (template.DoodadId == interactionTarget.TemplateId)
                                    {
                                        checking = true;
                                        Objectives[componentIndex]++;
                                        _log.Warn("[Quest] OnInteraction: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                    }
                                }
                                break;
                            }
                        default:
                            _log.Warn("[Quest] OnInteraction: character {0}, wants to do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                            break;
                    }
                }
            }
            Update(checking);
        }

        public void OnExpressFire(Npc npc, uint emotionId)
        {
            var checking = false;
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
                        case "QuestActObjExpressFire":
                            {
                                var expressKeyId = ExpressTextManager.Instance.GetExpressAnimId(emotionId);
                                var template = act.GetTemplate<QuestActObjExpressFire>();
                                if (template.ExpressKeyId == expressKeyId)
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn("[Quest] OnExpressEmotion: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                    }
                }
            }
            Update(checking);
        }

        public void OnLevelUp()
        {
            var checking = false;
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

                    checking = true;
                    Objectives[i]++;
                    _log.Warn("[Quest] OnLevelUp: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                }
            }
            Update(checking);
        }

        public void OnQuestComplete(uint questId)
        {
            var checking = false;
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
                                    checking = true;
                                    Objectives[i]++;
                                }

                                break;
                            }
                    }
                    _log.Warn("[Quest] OnQuestComplete: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                }
            }

            Update(checking);
        }

        public void OnEnterSphere(SphereQuest sphereQuest)
        {
            var checking = false;
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
                                    checking = act.Use(Owner, this, 0);
                                    Status = QuestStatus.Ready;
                                    ComponentId = components[componentIndex].Id;
                                    //Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                    _log.Warn("[Quest] OnEnterSphere: do it - {0}, ComponentId {1}, Step {2}, Status {3}, act.DetailType {4}", TemplateId, ComponentId, Step, Status, act.DetailType);
                                    Step++;
                                }
                                break;
                            }
                        default:
                            // здесь еще есть компоненты, которые не проверили (there are still components here that haven't been tested)
                            _log.Warn("[Quest] OnEnterSphere: wants to do it - {0}, ComponentId {1}, Step {2}, Status {3}, act.DetailType {4}", TemplateId, ComponentId, Step, Status, act.DetailType);
                            break;
                    }
                }
            }
            Update(checking);
        }

        public void OnCraft(Craft craft)
        {
            // TODO added for quest Id=6024
            var checking = false;
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
                        case "QuestActObjCraft":
                            {
                                var template = act.GetTemplate<QuestActObjCraft>();
                                if (template.CraftId == craft.Id)
                                {
                                    if (Objectives[componentIndex] < template.Count)
                                    {
                                        checking = true;
                                        Objectives[componentIndex]++;
                                    }
                                    else
                                    {
                                        checking = false;
                                    }
                                    _log.Warn("[Quest] QuestActObjCraft: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, checking {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, checking, act.DetailType);
                                }
                                break;
                            }
                    }
                }
            }
            Update(checking);
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
            for (var i = 0; i < ObjectiveCount; i++)
            {
                Objectives[i] = stream.ReadInt32();
            }

            Step = (QuestComponentKind)stream.ReadByte();
            QuestAcceptorType = (QuestAcceptorType)stream.ReadByte();
            ComponentId = stream.ReadUInt32();
            AcceptorType = stream.ReadUInt32();
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
            stream.Write((byte)QuestAcceptorType);
            stream.Write(ComponentId);
            stream.Write(AcceptorType);
            stream.Write(Time);
            return stream.GetBytes();
        }
        #endregion
    }
}

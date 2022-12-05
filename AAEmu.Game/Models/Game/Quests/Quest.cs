using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Enums;
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
        private readonly ISphereQuestManager _sphereQuestManager;
        private readonly IQuestManager _questManager;
        private readonly ITaskManager _taskManager;
        private readonly ISkillManager _skillManager;
        private readonly IExpressTextManager _expressTextManager;
        private readonly IWorldManager _worldManager;
        public long Id { get; set; }
        public uint TemplateId { get; set; }
        public IQuestTemplate Template { get; set; }
        public QuestStatus Status { get; set; }
        public int[] Objectives { get; set; }
        public QuestComponentKind Step { get; set; }
        public DateTime Time { get; set; }
        public ICharacter Owner { get; set; }
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
        public List<ItemCreationDefinition> QuestRewardItemsPool { get; set; }
        public int QuestRewardCoinsPool { get; set; }
        public int QuestRewardExpPool { get; set; }

        public uint GetActiveComponent()
        {
            return Template.GetFirstComponent(Step).Id;
        }

        public Quest(IQuestTemplate questTemplate, IQuestManager questManager, ISphereQuestManager sphereQuestManager, ITaskManager taskManager, ISkillManager skillManager, IExpressTextManager expressTextManager, IWorldManager worldManager)
        {
            _questManager = questManager;
            _sphereQuestManager = sphereQuestManager;
            _taskManager = taskManager;
            _skillManager = skillManager;
            _expressTextManager = expressTextManager;
            _worldManager = worldManager;

            if (questTemplate is not null)
            {
                TemplateId = questTemplate.Id;
                Template = questTemplate;
            }

            Objectives = new int[ObjectiveCount];
            SupplyItem = 0;
            EarlyCompletion = false;
            ExtraCompletion = false;
            ObjId = 0;
            QuestRewardItemsPool = new List<ItemCreationDefinition>();
        }

        public Quest() : this(
            null,
            QuestManager.Instance,
            SphereQuestManager.Instance,
            TaskManager.Instance,
            SkillManager.Instance,
            ExpressTextManager.Instance,
            WorldManager.Instance)
        {
        }

        public Quest(IQuestTemplate template) : this(
            template,
            QuestManager.Instance,
            SphereQuestManager.Instance,
            TaskManager.Instance,
            SkillManager.Instance,
            ExpressTextManager.Instance,
            WorldManager.Instance)
        {
        }

        private QuestStatus CalculateQuestStatus(QuestComponent currentComponent)
        {
            var questComponents = Template.Components.Values.ToList();
            var currentComponentIndex = questComponents.IndexOf(currentComponent);

            if (currentComponent.KindId is not QuestComponentKind.None and not QuestComponentKind.Start)
            {
                return Status;
            }

            // Let's take next component following Start or None
            var nextComponent = questComponents.ElementAt(currentComponentIndex + 1);

            if (nextComponent.KindId != QuestComponentKind.Supply)
            {
                var acts = nextComponent.ActTemplates;
                if (acts.Count == 0)
                {
                    return QuestStatus.Ready;
                }

                return nextComponent.KindId == QuestComponentKind.Progress
                    ? QuestStatus.Progress
                    : QuestStatus.Ready;
            }

            // This is a rare case scenario where a quest component supply kind is followed by a progress kind both with ActSupplyItem
            // Quest ids: 748, 1478, 2479, 5458

            var supplyComponent = nextComponent;
            // Component following Supply
            var componentAfterSupply = questComponents.ElementAt(currentComponentIndex + 2);

            if (componentAfterSupply.KindId == QuestComponentKind.Progress && componentAfterSupply.ActTemplates.OfType<QuestActSupplyItem>().Any())
            {
                // The specific quest 5458 fall in this category but the item supplied is different
                var componentAfterSupplySupplyItens = componentAfterSupply.ActTemplates.OfType<QuestActSupplyItem>().Select(i => i.ItemId).ToArray();
                var supplyComponentSupplyItens = supplyComponent.ActTemplates.OfType<QuestActSupplyItem>().Select(i => i.ItemId).ToArray();

                // Checks if the Itens provided in the Supply component act is the same for Progress component act
                if (!componentAfterSupplySupplyItens.Except(supplyComponentSupplyItens).Any())
                {
                    return QuestStatus.Ready;
                }
            }

            return componentAfterSupply.KindId == QuestComponentKind.Progress
                ? QuestStatus.Progress
                : QuestStatus.Ready;
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
                    var currentComponent = components[componentIndex];
                    var acts = _questManager.GetActs(currentComponent.Id);
                    CheckAcceptNpcs(acts, componentIndex, currentComponent, ref res, ref acceptNpc);

                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActConAcceptItem":
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
                                        ComponentId = currentComponent.Id;
                                        Status = CalculateQuestStatus(currentComponent);
                                        _log.Info($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    }
                                    else
                                    {
                                        _log.Warn($"[Quest] Start failed: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                        return false;
                                        //Not the NPC that is needed by the quest, the exit)
                                    }
                                    UseSkillAndBuff(currentComponent);
                                    break;
                                }
                            case "QuestActSupplyItem":
                                {
                                    // if SupplyItem = 0, we get the item
                                    res = act.Use(Owner, this, 0);
                                    Step = QuestComponentKind.Supply;
                                    // в процессе работы метода ItemGather  переключается на Progress (
                                    // In the process of ItemGather method operation switches to Progress)
                                    supply = res;
                                    // если было пополнение предметом, то на метод Update() не переходить (
                                    // If there was a replenishment of an object, then do not go to the Update() method)
                                    _log.Warn("[Quest] Start: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
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
                                    return true;
                                }
                            default:
                                {
                                    //case "QuestActObjTalk":
                                    //case "QuestActObjTalkNpcGroup":
                                    supply = true; // прерываем цикл и на метод Update() не переходим (interrupt the cycle and do not go to the update() method)
                                    _log.Warn("[Quest] Start: character {0}, default don't do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                    // TODO added for quest Id=4402
                                    goto EndLoop;
                                }
                        }
                    }
                }
            }

            EndLoop:
            Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));

            if (Status == QuestStatus.Progress && !supply)
            {
                Step = QuestComponentKind.Progress; // потому, что уже стоял на Fail (Because he was already standing on Fail)
                Update(res);
            }

            return res;
        }

        /// <summary>
        /// Метод предназначен для вызова из скрипта QuestCmd, команда /quest add <questId>
        /// The method is used to call from the QuestCmd script, the command /quest add <questId>)
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
                    var acts = _questManager.GetActs(components[componentIndex].Id);
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            default:
                                _log.Warn($"[Quest] Start: character {Owner.Name}, default don't do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            case "QuestActConAcceptItem":
                            case "QuestActConAcceptDoodad": // старт ежедневного квеста (start of the daily quest)
                            case "QuestActConAcceptNpcKill":
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                if (res)
                                {
                                    ComponentId = components[componentIndex].Id;
                                    Status = CalculateQuestStatus(components[componentIndex]);
                                    _log.Warn("[Quest] Start: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                }
                                else
                                {
                                    _log.Warn($"[Quest] Start failed: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    return; // не тот Npc, что нужен по квесту, выход (not the Npc that is needed on the quest, exit)
                                }
                                UseSkillAndBuff(components[componentIndex]);
                                break;
                            case "QuestActConAcceptNpc":
                                {
                                    // не проверяем Npc при взятии квеста (do not check the Npc when taking the quest)
                                    act.Use(Owner, this, 0);
                                    ComponentId = components[componentIndex].Id;
                                    Status = CalculateQuestStatus(components[componentIndex]);
                                    _log.Warn("[Quest] Start: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, res {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, res, act.DetailType);
                                    UseSkillAndBuff(components[componentIndex]);
                                    SetNpcAggro(components[componentIndex]);
                                    break;
                                }
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    res = act.Use(Owner, this, 0); // получим предмет (get the item)
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
                                    return;
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
                            _taskManager.Schedule(QuestTask, TimeSpan.FromSeconds(delay));
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
                var questActObjTalk = false; // TODO: added for quest Id=2037
                var questActObjInteraction = false; // TODO: added for quest Id=3353
                var reportNpc = false;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var currentComponent = components[componentIndex];
                    var acts = _questManager.GetActs(currentComponent.Id);
                    CheckReportNpcs(acts, componentIndex, currentComponent, ref complete, ref reportNpc);

                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Progress:
                                {
                                    // if SupplyItem = 0, we get the item
                                    complete = act.Use(Owner, this, SupplyItem);
                                    break;
                                }
                            case "QuestActSupplyItem" when Step == QuestComponentKind.Supply:
                                {
                                    complete = act.Use(Owner, this, SupplyItem);
                                    _log.Warn("[Quest] Update: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, complete {5}, act.DetailType {6}", Owner.Name, TemplateId, ComponentId, Step, Status, complete, act.DetailType);
                                    var next = QuestComponentKind.Progress;
                                    var componentnext = Template.GetFirstComponent(next);
                                    if (componentnext == null)
                                        break;
                                    var actsnext = _questManager.GetActs(componentnext.Id);
                                    foreach (var qa in actsnext)
                                    {
                                        var questSupplyItem = (QuestActSupplyItem)_questManager.GetActTemplate(act.DetailId, "QuestActSupplyItem");
                                        var questItemGather = (QuestActObjItemGather)_questManager.GetActTemplate(qa.DetailId, "QuestActObjItemGather");
                                        switch (qa.DetailType)
                                        {
                                            case "QuestActObjItemGather" when questSupplyItem.ItemId == questItemGather.ItemId:
                                                Owner.Inventory.Bag.GetAllItemsByTemplate(questSupplyItem.ItemId, -1, out _, out Objectives[componentIndex]);
                                                complete = qa.Use(Owner, this, Objectives[componentIndex]);
                                                Step = next;
                                                ComponentId = currentComponent.Id;
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
                                {
                                    if (reportNpc)
                                    {
                                        // мы уже проверяли этот пункт, поэтому пропускаем (We have already checked this item, so we miss)
                                    }
                                    else
                                    {
                                        complete = act.Use(Owner, this, Objectives[componentIndex]);
                                    }
                                    // проверка результатов на валидность (Validation of results)
                                    if (complete)
                                    {
                                        UseSkillAndBuff(currentComponent);
                                        // компонент - выполнен, мы у нужного Npc (component - done, we're at the right Npc)
                                        Status = QuestStatus.Ready;
                                        _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                        Owner.Quests.Complete(TemplateId, 0);
                                        return;
                                    }
                                    // компонент - выполнен (component - done)
                                    Status = QuestStatus.Ready;
                                    _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActConAutoComplete":
                                {
                                    // компонент - выполнен (component - ready)
                                    complete = true;
                                    Status = QuestStatus.Ready;
                                    _log.Warn($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                    Owner.Quests.Complete(TemplateId, 0);
                                    return;
                                }
                            case "QuestActObjSphere":
                                {
                                    // подготовим работу QuestSphere (prepare QuestSphere's work)
                                    //var template = act.GetTemplate<QuestActObjSphere>();
                                    Status = QuestStatus.Progress;
                                    ComponentId = components[componentIndex].Id;
                                    var spheres = _sphereQuestManager.GetQuestSpheres(ComponentId);
                                    if (spheres != null)
                                    {
                                        foreach (var sphere in spheres)
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

                                            _sphereQuestManager.AddSphereQuestTrigger(sphereQuestTrigger);
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

                                    // если сфера по какой-то причине отсутствует, будем считать, что мы её посетили
                                    // if the sphere is missing for some reason, we will assume that we have visited it
                                    Status = QuestStatus.Progress;
                                    Owner.SendMessage($"[Quest] {Owner.Name}, quest {TemplateId}, Sphere not found - skipped...");
                                    _log.Warn($"[Quest] QuestActObjSphere: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, act.DetailType {act.DetailType}");
                                    _log.Warn($"[Quest] QuestActObjSphere: Sphere not found with cquest {ComponentId} in quest_sign_spheres!");
                                    complete = true;
                                    break;
                                }
                            case "QuestActEtcItemObtain":
                                {
                                    // TODO: added for quest Id=882.
                                    // ничего не делаем (We're not doing anything)
                                    break;
                                }
                            case "QuestActObjTalk":
                                {
                                    var template = act.GetTemplate<QuestActObjTalk>();
                                    // TODO: added for quest Id=2037
                                    complete = act.Use(Owner, this, Objectives[componentIndex]);
                                    completes[componentIndex] = complete; // продублируем информацию (let's duplicate the information)
                                    // установим в true для дальнейшей проверки (set in True for further verification)
                                    questActObjTalk = true;
                                    switch (currentComponent.NpcAiId)
                                    {
                                        case QuestNpcAiName.FollowPath:
                                            {
                                                var route = currentComponent.AiPathName;
                                                var npcs = WorldManager.Instance.GetAllNpcs();
                                                foreach (var npc in npcs)
                                                {
                                                    if (npc.TemplateId != template.NpcId) { continue; }
                                                    if (npc.IsInPatrol) { break; }
                                                    switch (currentComponent.AiPathTypeId)
                                                    {
                                                        case PathType.Remove:
                                                            npc.Simulation.Cycle = false;
                                                            npc.Simulation.Remove = true;
                                                            npc.IsInPatrol = true;
                                                            npc.Simulation.RunningMode = true;
                                                            npc.Simulation.MoveToPathEnabled = false;
                                                            npc.Simulation.MoveFileName = route;
                                                            npc.Simulation.GoToPath(npc, true);
                                                            break;
                                                        case PathType.None:
                                                            break;
                                                        case PathType.Idle:
                                                            break;
                                                        case PathType.Loop:
                                                            npc.Simulation.Cycle = true;
                                                            npc.Simulation.Remove = false;
                                                            npc.IsInPatrol = true;
                                                            npc.Simulation.RunningMode = true;
                                                            npc.Simulation.MoveToPathEnabled = false;
                                                            npc.Simulation.MoveFileName = route;
                                                            npc.Simulation.GoToPath(npc, true);
                                                            break;
                                                        default:
                                                            throw new ArgumentOutOfRangeException();
                                                    }
                                                    break;
                                                }

                                                break;
                                            }
                                        case QuestNpcAiName.None:
                                            break;
                                        case QuestNpcAiName.FollowUnit:
                                            break;
                                        case QuestNpcAiName.AttackUnit:
                                            break;
                                        case QuestNpcAiName.GoAway:
                                            break;
                                        case QuestNpcAiName.RunCommandSet:
                                            break;
                                        default:
                                            throw new ArgumentOutOfRangeException();
                                    }
                                    break;
                                }
                            case "QuestActObjInteraction":
                                {
                                    // TODO: added for quest Id=3353
                                    complete = act.Use(Owner, this, Objectives[componentIndex]);
                                    completes[componentIndex] = complete;
                                    // установим в true для дальнейшей проверки (set in True for further verification)
                                    questActObjInteraction = true;
                                    break;
                                }
                            case "QuestActSupplyRemoveItem":
                                {
                                    // TODO: added for quest Id=2037
                                    // ничего не делаем (We're not doing anything)
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
                                {
                                    //case "QuestActObjItemUse":
                                    //case "QuestActObjMonsterHunt":
                                    //case "QuestActObjMonsterGroupHunt":
                                    // эти акты могут быть парными: ItemGather & MonsterHunt & MonsterGroupHunt & Interaction
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

                    if (Step == QuestComponentKind.Progress && complete)
                        ComponentId = currentComponent.Id;

                    if (completes[componentIndex] || complete)
                    {
                        UseSkillAndBuff(currentComponent);
                    }
                }

                // TODO added for quest Id=1135 - достаточно выполнение одного элемента itemGather || monsterHunt (it is enough to execute one element itemGather || monsterHunt)
                // TODO added for quest Id=1511 - обязательно выполнение обеих элементов itemGather (it is obligatory to perform both itemGather elements)
                if (components.Length > 1)
                {
                    // TODO added for quest id=4294 - нужен только itemGather, а ItemUse не нужен (only itemGather is needed, and ItemUse is not needed)
                    complete = Template.Score > 0 ? completes.Any(b => b) : completes.All(b => b);
                    Status = complete ? QuestStatus.Ready : QuestStatus.Progress;

                    if (questActObjTalk || questActObjInteraction) // TODO: added for quest Id=2037, Id=3353
                    {
                        for (var i = 0; i < components.Length; i++)
                        {
                            if (Objectives[i] > 0)
                            {
                                complete = true;
                                Status = QuestStatus.Ready;
                            }
                            else
                            {
                                complete = false;
                                Status = QuestStatus.Progress;
                                break;
                            }
                        }
                    }
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

        private void CheckAcceptNpcs(IQuestAct[] acts, int componentIndex, QuestComponent currentComponent, ref bool res, ref bool acceptNpc)
        {
            var questActConAcceptNpc = acts.All(a => a.DetailType == "QuestActConAcceptNpc");

            if (acts.Length <= 0 || !questActConAcceptNpc) { return; }

            // оказывается может быть несколько Npc с которыми можно заключить квест! (It turns out that there may be several NPCs with which you can make a quest!)
            var targetNpcMatch = acts.Any(t => t.Use(Owner, this, Objectives[componentIndex]));
            if (!targetNpcMatch)
            {
                _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
                return;
            }

            res = true;
            acceptNpc = true;
            ComponentId = currentComponent.Id;
            Status = CalculateQuestStatus(currentComponent);

            _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
            UseSkillAndBuff(currentComponent);
            SetNpcAggro(currentComponent);
        }
       
        private void CheckReportNpcs(IQuestAct[] acts, int componentIndex, QuestComponent currentComponent, ref bool res, ref bool acceptNpc)
        {
            var questActConReportNpc = acts.All(a => a.DetailType == "QuestActConReportNpc");

            if (acts.Length <= 0 || !questActConReportNpc) { return; }

            // оказывается может быть несколько Npc с которыми можно заключить квест! (It turns out that there may be several NPCs with which you can make a quest!)
            var targetNpcMatch = acts.Any(t => t.Use(Owner, this, Objectives[componentIndex]));
            if (!targetNpcMatch)
            {
                _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
                return;
            }

            res = true;
            acceptNpc = true;
            ComponentId = currentComponent.Id;
            Status = CalculateQuestStatus(currentComponent);

            _log.Warn($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
            UseSkillAndBuff(currentComponent);
            SetNpcAggro(currentComponent);
        }

        /// <summary>
        /// Use Skill на себя или на Npc, с которым взаимодействуем (Use Skill on yourself or on the Npc you interact with)
        /// </summary>
        /// <param name="component"></param>
        private void UseSkillAndBuff(QuestComponent component)
        {
            if (component == null) { return; }
            UseSkill(component);
            UseBuff(component);
        }

        private void UseBuff(QuestComponent component)
        {
            if (component.BuffId > 0)
            {
                Owner.Buffs.AddBuff(new Buff(Owner, Owner, SkillCaster.GetByType(SkillCasterType.Unit), _skillManager.GetBuffTemplate(component.BuffId), null, DateTime.UtcNow));
            }
        }

        private void UseSkill(QuestComponent component)
        {
            if (component.SkillId > 0)
            {
                if (component.SkillSelf)
                {
                    Owner.UseSkill(component.SkillId, Owner);
                }
                else if (component.NpcId > 0)
                {
                    var npc = _worldManager.GetNpcByTemplateId(component.NpcId);
                    npc?.UseSkill(component.SkillId, npc);
                }
            }
        }

        private void SetNpcAggro(QuestComponent component)
        {
            if (component == null) { return; }
            if (component.NpcAiId == QuestNpcAiName.AttackUnit)
            {
                if (component.NpcId > 0)
                {
                    var npc = _worldManager.GetNpcByTemplateId(component.NpcId);
                    npc?.SetFaction(FactionsEnum.Monstrosity); // TODO mb Hostile
                }
            }
        }

        public void DistributeRewards()
        {
            // Distribute Items if needed
            if (QuestRewardItemsPool.Count > 0)
            {
                // TODO: Add a way to distribute honor or vocation badges in mail as well 

                if (Owner.Inventory.Bag.FreeSlotCount < QuestRewardItemsPool.Count)
                {
                    var mails = MailManager.Instance.CreateQuestRewardMails(Owner, this, QuestRewardItemsPool, QuestRewardCoinsPool);
                    QuestRewardCoinsPool = 0; // Coins will be distributed in mail if any mail needed to be send, so set to zero again
                    foreach (var mail in mails)
                        if (!mail.Send())
                            Owner.SendErrorMessage(ErrorMessageType.MailUnknownFailure);

                    Owner.SendPacket(new SCQuestRewardedByMailPacket(new uint[] { TemplateId }));
                }
                else
                {
                    foreach (var item in QuestRewardItemsPool)
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

                QuestRewardItemsPool.Clear();
            }

            // Add XP
            if (QuestRewardExpPool > 0)
            {
                Owner.AddExp(QuestRewardExpPool, true);
                QuestRewardExpPool = 0;
            }

            // Add copper coins
            if (QuestRewardCoinsPool > 0)
            {
                Owner.ChangeMoney(SlotType.None, SlotType.Inventory, QuestRewardCoinsPool);
                QuestRewardCoinsPool = 0;
            }
        }

        public uint Complete(int selected)
        {
            var res = false;
            var reportNpc = false;
            //var step = QuestComponentKind.Ready; // покажем, что заканчиваем квест (let's show you that we're finishing the quest)
            for (Step = QuestComponentKind.Ready; Step <= QuestComponentKind.Reward; Step++)
            {
                if (Step >= QuestComponentKind.Drop)
                    Status = QuestStatus.Completed;

                var components = Template.GetComponents(Step);
                if (components.Length == 0)
                    continue;

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var currentComponent = components[componentIndex];
                    var acts = _questManager.GetActs(currentComponent.Id);
                    CheckReportNpcs(acts, componentIndex, currentComponent, ref res, ref reportNpc);
                    
                    if (Step == QuestComponentKind.Ready)
                        ComponentId = currentComponent.Id;

                    var selective = 0;
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActConReportJournal":
                            case "QuestActConReportNpc":
                                if (reportNpc)
                                {
                                    // мы уже проверяли этот пункт, поэтому пропускаем (We have already checked this item, so we miss)
                                    break;
                                }
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                if (ComponentId == 0)
                                    ComponentId = currentComponent.Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            case "QuestActSupplySelectiveItem":
                                {
                                    selective++;
                                    if (selective == selected)
                                    {
                                        res = act.Use(Owner, this, Objectives[componentIndex]);
                                        if (ComponentId == 0)
                                            ComponentId = currentComponent.Id;
                                        _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                    }
                                    break;
                                }
                            case "QuestActSupplyItem":
                                var prevStep = Step; // сохраним, так как Step изменится на Progress (we will save it, since Step will change to Progress)
                                res = act.Use(Owner, this, 0); // всегда получаем предметы в конце квеста (always get items at the end of the quest)
                                Step = prevStep;                               
                                if (ComponentId == 0)
                                    ComponentId = currentComponent.Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            case "QuestActConAutoComplete":
                                res = true;
                                if (ComponentId == 0)
                                    ComponentId = currentComponent.Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
                                break;
                            default:
                                res = act.Use(Owner, this, Objectives[componentIndex]);
                                if (ComponentId == 0)
                                    ComponentId = currentComponent.Id;
                                _log.Warn($"[Quest] Complete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {act.DetailType}");
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

        public int GetCustomExp() { return GetCustomSupplies("exp"); }

        public int GetCustomCopper() { return GetCustomSupplies("copper"); }

        private int GetCustomSupplies(string supply)
        {
            var value = 0;
            var component = Template.GetFirstComponent(QuestComponentKind.Reward);
            if (component == null)
            {
                return 0;
            }

            var acts = _questManager.GetActs(component.Id);
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
                    var acts = _questManager.GetActs(components[componentIndex].Id);
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
                            case "QuestActConAcceptItem":
                                {
                                    // TODO: added for quest Id=5700
                                    var template = act.GetTemplate<QuestActConAcceptItem>();
                                    if (template.DropWhenDestroy || template.Cleanup)
                                    {
                                        var count = Owner.Inventory.GetItemsCount(template.ItemId);
                                        Objectives[componentIndex] = count;
                                        Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, Objectives[componentIndex], null);
                                        _log.Warn("[Quest] RemoveQuestItems: character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}, ItemId {6}, Count {7}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType, template.ItemId, count);
                                    }
                                    break;
                                }
                            case "QuestActSupplyRemoveItem":
                                {
                                    // TODO: added for quest Id=2037
                                    var template = act.GetTemplate<QuestActSupplyRemoveItem>();
                                    Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestRemoveSupplies, template.ItemId, template.Count, null);
                                    _log.Warn("[Quest] RemoveQuestItems:QuestActSupplyRemoveItem character {0}, do it - {1}, ComponentId {2}, Step {3}, Status {4}, act.DetailType {5}, ItemId {6}, Count {7}", Owner.Name, TemplateId, ComponentId, Step, Status, act.DetailType, template.ItemId, template.Count);
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

            for (var step = QuestComponentKind.Ready; step < QuestComponentKind.Reward; step++)
            {
                var component = Template.GetFirstComponent(step);
                UseSkillAndBuff(component);
            }

            if (update)
                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));

            RemoveQuestItems();
            for (var i = 0; i < ObjectiveCount; i++)
                Objectives[i] = 0;
        }

        #region Events

        public void OnReportToNpc(Npc npc, int selected)
        {
            var checking = false;
            Step = QuestComponentKind.Ready;
            var components = Template.GetComponents(Step);
            if (components.Length == 0)
                return;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                    _log.Warn($"[Quest] OnReportToNpc: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                    _log.Warn($"[Quest] OnReportToDoodad: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                }
                                break;
                            }
                    }
                    _log.Warn($"[Quest] OnTalkMade: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                }
                                break;
                            }
                        case "QuestActObjMonsterGroupHunt":
                            {
                                var template = act.GetTemplate<QuestActObjMonsterGroupHunt>();
                                if (_questManager.CheckGroupNpc(template.QuestMonsterGroupId, npc.TemplateId))
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                }
                                break;
                            }
                    }
                    _log.Warn($"[Quest] OnKill: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                    if (tmpStep == QuestComponentKind.Supply)
                                    {
                                        _log.Warn($"[Quest] OnItemGather: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                                        Step = tmpStep;
                                        return; // возврат в метод Start() (return to Start() method)
                                    }
                                }
                                return; // возврат в метод Update() (return to Update() method)
                            }
                        case "QuestActObjItemGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGather>();
                                if (template.ItemId == item.TemplateId)
                                {
                                    checking = true;
                                    Objectives[componentIndex] += count;
                                }
                                break;
                            }
                        case "QuestActObjItemGroupGather":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupGather>();
                                if (_questManager.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    checking = true;
                                    Objectives[componentIndex] += count;
                                }
                                break;
                            }
                            // TODO не работал из-за этого квест ID=3327, "Goblin Treasure", 39, "Sanddeep", "Sanddeep"
                            //// TODO added for quest Id=4402
                            //// TODO added for quest Id=266
                            //default:
                            //    {
                            //        goto exit;
                            //    }
                    }
                    _log.Warn($"[Quest] OnItemGather: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                }
                //exit:;
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                }
                                break;
                            }
                        case "QuestActObjItemGroupUse":
                            {
                                var template = act.GetTemplate<QuestActObjItemGroupUse>();
                                if (_questManager.CheckGroupItem(template.ItemGroupId, item.TemplateId))
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                }
                                break;
                            }
                    }
                    _log.Warn($"[Quest] OnItemUse: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                        _log.Warn($"[Quest] OnInteraction: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                                    }
                                }
                                break;
                            }
                        default:
                            _log.Warn($"[Quest] OnInteraction: character {Owner.Name}, wants to do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
                foreach (var act in acts)
                {
                    switch (act.DetailType)
                    {
                        case "QuestActObjExpressFire":
                            {
                                var expressKeyId = _expressTextManager.GetExpressAnimId(emotionId);
                                var template = act.GetTemplate<QuestActObjExpressFire>();
                                if (template.ExpressKeyId == expressKeyId)
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                }
                                break;
                            }
                    }
                    _log.Warn($"[Quest] OnExpressEmotion: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                }
            }
            Update(checking);
        }

        public void OnLevelUp()
        {
            var checking = false;

            // для предотвращения зацикливания
            //Step = QuestComponentKind.Progress;
            var step = Step;
            for (; step <= QuestComponentKind.Progress; step++)
            {
                //if (step is QuestComponentKind.Fail or QuestComponentKind.Drop)
                //    continue;

                var components = Template.GetComponents(step);
                if (components.Length == 0) { continue; }

                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var acts = _questManager.GetActs(components[componentIndex].Id);
                    foreach (var act in acts)
                    {
                        switch (act.DetailType)
                        {
                            case "QuestActObjLevel":
                                {
                                    var template = act.GetTemplate<QuestActObjLevel>();
                                    if (template.Level >= Owner.Level)
                                    {
                                        continue;
                                    }

                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn(
                                        $"[Quest] OnLevelUp: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            case "QuestActObjAbilityLevel":
                                {
                                    checking = true;
                                    Objectives[componentIndex]++;
                                    _log.Warn($"[Quest] OnLevelUp: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                                    break;
                                }
                            default:
                                _log.Warn($"[Quest] OnInteraction: character {Owner.Name}, wants to do it - {TemplateId}, ComponentId {ComponentId}, Step {step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                                break;
                        }
                    }
                }
            }

            Update(checking);
        }

        public void OnQuestComplete(uint questId)
        {
            var checking = false;
            var component = Template.GetFirstComponent(Step);
            if (component != null)
            {
                var acts = _questManager.GetActs(component.Id);
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
                    _log.Warn($"[Quest] OnQuestComplete: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                    _log.Warn($"[Quest] OnEnterSphere: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                                    Step++;
                                }
                                break;
                            }
                        default:
                            // здесь еще есть компоненты, которые не проверили (there are still components here that haven't been tested)
                            _log.Warn($"[Quest] OnEnterSphere: character {Owner.Name}, wants to do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
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
                var acts = _questManager.GetActs(components[componentIndex].Id);
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
                                }
                                break;
                            }
                    }
                    _log.Warn($"[Quest] QuestActObjCraft: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, checking {checking}, act.DetailType {act.DetailType}");
                }
            }
            Update(checking);
        }

        #endregion

        public void RecalcObjectives(bool send = true)
        {
            var component = Template.GetFirstComponent(Step);
            if (component == null)
            {
                return;
            }

            var acts = _questManager.GetActs(component.Id);
            for (var i = 0; i < acts.Length; i++)
            {
                var act = acts[i];

                switch (act.DetailType)
                {
                    case "QuestActSupplyItem":
                        {
                            var template = acts[i].GetTemplate<QuestActSupplyItem>();
                            Objectives[i] = Owner.Inventory.GetItemsCount(template.ItemId);
                            SupplyItem = Objectives[i]; // the same as Objectives, but for QuestActSupplyItem
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
                            SupplyItem = Objectives[i]; // the same as Objectives, but for QuestActSupplyItem
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
                            foreach (var itemId in _questManager.GetGroupItems(template.ItemGroupId))
                            {
                                Objectives[i] += Owner.Inventory.GetItemsCount(itemId);
                            }
                            SupplyItem = Objectives[i]; // the same as Objectives, but for QuestActSupplyItem

                            if (Objectives[i] > template.Count) // TODO check to overtime
                            {
                                Objectives[i] = template.Count;
                            }

                            break;
                        }
                }
                _log.Warn($"[Quest] RecalcObjectives: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, act.DetailType {act.DetailType}");
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

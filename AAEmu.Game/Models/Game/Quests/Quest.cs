using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Quests;

namespace AAEmu.Game.Models.Game.Quests;

public partial class Quest : PacketMarshaler
{
    private const int MaxObjectiveCount = 5;
    private readonly ISphereQuestManager _sphereQuestManager;
    private readonly IQuestManager _questManager;
    private readonly ITaskManager _taskManager;
    private readonly ISkillManager _skillManager;
    private readonly IExpressTextManager _expressTextManager;
    private readonly IWorldManager _worldManager;
    private QuestComponentKind _step;
    
    /// <summary>
    /// DB ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Quest Template Id
    /// </summary>
    public uint TemplateId { get; set; }

    /// <summary>
    /// Quest Template
    /// </summary>
    public IQuestTemplate Template { get; set; }

    /// <summary>
    /// Objective counters for the Progress step
    /// </summary>
    internal int[] Objectives { get; set; }

    /// <summary>
    /// Used to check Progress step
    /// </summary>
    public List<bool> ProgressStepResults { get; set; } = new();

    /// <summary>
    /// Current Quest Status
    /// </summary>
    public QuestStatus Status { get; set; }

    /// <summary>
    /// Current Quest Step
    /// </summary>
    public QuestComponentKind Step
    {
        get => _step;
        set => SetStep(value);
    }

    /// <summary>
    /// Objective Condition
    /// </summary>
    public QuestConditionObj Condition { get; set; }

    /// <summary>
    /// Is this quest ready to be turned in at a NPC
    /// </summary>
    public bool ReadyToReportNpc { get; set; }

    /// <summary>
    /// End time for timed quests
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// Owning character of this Quest Object
    /// </summary>
    public ICharacter Owner { get; set; }

    /// <summary>
    /// Remaining time for this quest in milliseconds
    /// </summary>
    private int LeftTime => Time > DateTime.UtcNow ? (int)(Time - DateTime.UtcNow).TotalMilliseconds : -1;
    private int SupplyItem { get; set; }

    /// <summary>
    /// DoodadId used in the Quest Packet
    /// </summary>
    public long DoodadId { get; set; }

    /// <summary>
    /// ObjId used in the Quest Packet (3 instances)
    /// </summary>
    private long ObjId { get; set; }

    // TODO: Check how this is actually supposed to behave in the packets
    /// <summary>
    /// ComponentId used in the SCQuestContext Packet
    /// </summary>
    public uint ComponentId { get; set; }

    /// <summary>
    /// Helper var for tracking the component we're working with
    /// </summary>
    public uint CurrentComponentId { get; set; }

    /// <summary>
    /// AcceptorType used for SCQuestContext
    /// </summary>
    public QuestAcceptorType QuestAcceptorType { get; set; }

    /// <summary>
    /// Acceptor Template Id of the QuestAcceptorType source where we got this quest from, used by SCQuestContext
    /// </summary>
    public uint AcceptorId { get; set; }

    /// <summary>
    /// Item pool of rewards
    /// </summary>
    public List<ItemCreationDefinition> QuestRewardItemsPool { get; set; }

    /// <summary>
    /// Item pool of items that are included in the cleanup process of this quest
    /// </summary>
    public List<ItemCreationDefinition> QuestCleanupItemsPool { get; set; }

    /// <summary>
    /// Money reward for this quest
    /// </summary>
    public int QuestRewardCoinsPool { get; set; }

    /// <summary>
    /// Exp reward for this quest
    /// </summary>
    public int QuestRewardExpPool { get; set; }

    /// <summary>
    /// Current QuestStep, or null if the current step is invalid
    /// </summary>
    public QuestStep CurrentStep => QuestSteps.GetValueOrDefault(Step);

    /// <summary>
    /// Set to false if item rewards have been disabled by objectives (mostly used by Aggro quests)
    /// </summary>
    public bool AllowItemRewards { get; set; } = true;

    /// <summary>
    /// Percent that the reward should be scaled to (mostly used by Aggro quests)
    /// </summary>
    public double QuestRewardRatio { get; set; } = 1.0;

    private bool _requestEvaluationFlag;
    /// <summary>
    /// Set if this quests is requesting a re-evaluation of its steps/components/acts to check if it has been completed
    /// Set by the RequestEvaluation function, call after objective changes
    /// </summary>
    private bool RequestEvaluationFlag
    {
        get => _requestEvaluationFlag;
        set
        {
            if (_requestEvaluationFlag == value)
                return;
            _requestEvaluationFlag = value;
            if (value)
            {
                _questManager.EnqueueEvaluation(this);
            }
        }
    }

    private bool _skipUpdatePacket;

    public void SkipUpdatePackets()
    {
        _skipUpdatePacket = true;
    }

    /// <summary>
    /// The last chosen selective reward index for this quest
    /// </summary>
    public int SelectedRewardIndex { get; set; }

    /// <summary>
    /// Create Quest object
    /// </summary>
    /// <param name="questTemplate"></param>
    /// <param name="owner"></param>
    /// <param name="questManager"></param>
    /// <param name="sphereQuestManager"></param>
    /// <param name="taskManager"></param>
    /// <param name="skillManager"></param>
    /// <param name="expressTextManager"></param>
    /// <param name="worldManager"></param>
    public Quest(IQuestTemplate questTemplate, ICharacter owner, IQuestManager questManager, ISphereQuestManager sphereQuestManager,
        ITaskManager taskManager, ISkillManager skillManager, IExpressTextManager expressTextManager,
        IWorldManager worldManager)
    {
        Owner = owner;
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

            CreateQuestSteps();
        }

        _step = QuestComponentKind.Invalid;
        Status = QuestStatus.Invalid;

        Objectives = new int[MaxObjectiveCount];
        SupplyItem = 0;
        ObjId = 0;
        QuestRewardItemsPool = new List<ItemCreationDefinition>();
        QuestCleanupItemsPool = new List<ItemCreationDefinition>();
        ReadyToReportNpc = false;

        InitializeQuestActs();
    }

    public Quest(ICharacter owner) : this(null, owner, QuestManager.Instance, SphereQuestManager.Instance, TaskManager.Instance, SkillManager.Instance, ExpressTextManager.Instance, WorldManager.Instance)
    {
        // Nothing extra
    }

    public Quest(IQuestTemplate template, ICharacter owner) : this(template, owner, QuestManager.Instance, SphereQuestManager.Instance, TaskManager.Instance, SkillManager.Instance, ExpressTextManager.Instance, WorldManager.Instance)
    {
        // Nothing Extra
    }

    private QuestStatus CalculateQuestStatus(QuestComponentTemplate currentComponent)
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
            if (nextComponent.ActTemplates.Count == 0)
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

        if (componentAfterSupply.KindId == QuestComponentKind.Progress && componentAfterSupply.ActTemplates.Any(x => x is QuestActSupplyItem))
        {
            // The specific quest 5458 fall in this category but the item supplied is different
            var componentAfterSupplySupplyItems = componentAfterSupply.ActTemplates.OfType<QuestActSupplyItem>().Select(i => i?.ItemId ?? 0).ToArray();
            var supplyComponentSupplyItems = supplyComponent.ActTemplates.OfType<QuestActSupplyItem>().Select(i => i?.ItemId ?? 0).ToArray();

            // Checks if the Items provided in the Supply component act is the same for Progress component act
            if (!componentAfterSupplySupplyItems.Except(supplyComponentSupplyItems).Any())
            {
                return QuestStatus.Ready;
            }
        }

        return componentAfterSupply.KindId == QuestComponentKind.Progress
            ? QuestStatus.Progress
            : QuestStatus.Ready;
    }

/*    
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
                                Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
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
                                            Owner.Inventory.Bag.GetAllItemsByTemplate(questSupplyItem.ItemId, -1, out _, out var newObjectives);
                                            qa.SetObjective(this, newObjectives);
                                            complete = qa.Use(Owner, this, newObjectives);
                                            Step = next;
                                            ComponentId = currentComponent.Id;
                                            break;
                                        default:
                                            complete = false;
                                            break;
                                    }
                                    Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
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
                                    complete = act.Use(Owner, this, act.GetObjective(this));
                                }
                                // проверка результатов на валидность (Validation of results)
                                if (complete)
                                {
                                    UseSkillAndBuff(currentComponent);
                                    // компонент - выполнен, мы у нужного Npc (component - done, we're at the right Npc)
                                    Status = QuestStatus.Ready;
                                    Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                    Owner.Quests.CompleteQuest(TemplateId, 0);
                                    return;
                                }
                                // компонент - выполнен (component - done)
                                Status = QuestStatus.Ready;
                                Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                break;
                            }
                        case "QuestActConAutoComplete":
                            {
                                // компонент - выполнен (component - ready)
                                complete = true;
                                Status = QuestStatus.Ready;
                                Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                                Owner.Quests.CompleteQuest(TemplateId, 0);
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
                                            Logger.Debug($"[Quest] QuestActObjSphere: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                            Logger.Debug($"[Quest] QuestActObjSphere: Sphere not found with cquest {components[componentIndex].Id} in quest_sign_spheres.json!");
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
                                    Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
                                    return;
                                }

                                // если сфера по какой-то причине отсутствует, будем считать, что мы её посетили
                                // if the sphere is missing for some reason, we will assume that we have visited it
                                Status = QuestStatus.Progress;
                                Owner.SendMessage($"[Quest] {Owner.Name}, quest {TemplateId}, Sphere not found - skipped...");
                                Logger.Debug($"[Quest] QuestActObjSphere: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, act.DetailType {act.DetailType}");
                                Logger.Debug($"[Quest] QuestActObjSphere: Sphere not found with cquest {ComponentId} in quest_sign_spheres!");
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
                                complete = act.Use(Owner, this, act.GetObjective(this));
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
                                                        throw new NotSupportedException(nameof(currentComponent.AiPathTypeId));
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
                                        throw new NotSupportedException(nameof(currentComponent.NpcAiId));
                                }
                                break;
                            }
                        case "QuestActObjInteraction":
                            {
                                // TODO: added for quest Id=3353
                                complete = act.Use(Owner, this, act.GetObjective(this));
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
                                if (act.GetObjective(this) == 0)
                                    act.SetObjective(this, Owner.Inventory.GetItemsCount(template.ItemId));
                                complete = act.Use(Owner, this, act.GetObjective(this));
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
                                complete = act.Use(Owner, this, act.GetObjective(this));
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
                    Logger.Debug($"[Quest] Update: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, complete {complete}, act.DetailType {act.DetailType}");
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

            var stepResult = GetQuestObjectiveStatus();
            if (complete && !(stepResult >= QuestObjectiveStatus.Overachieved || (Template.LetItDone && stepResult >= QuestObjectiveStatus.QuestComplete)))
            {
                break; // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
                       // (the quest can be passed, but we do not let it end when it reaches 100% until we come to the Npc to pass the quest)
            }
            if (!complete)
            {
                break;
            }
        }
        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
*/

/*
    private void CheckAcceptNpcs(IQuestAct[] acts, int componentIndex, QuestComponent currentComponent, ref bool res, ref bool acceptNpc)
    {
        var questActConAcceptNpc = acts.All(a => a.DetailType == "QuestActConAcceptNpc");

        if (acts.Length <= 0 || !questActConAcceptNpc) { return; }

        // оказывается может быть несколько Npc с которыми можно заключить квест! (It turns out that there may be several NPCs with which you can make a quest!)
        var targetNpcMatch = acts.Any(t => t.Use(Owner, this, Objectives[componentIndex]));
        if (!targetNpcMatch)
        {
            Logger.Warn($"[Quest] Start: character {Owner.Name}, NPC doesn't match - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
            return;
        }

        res = true;
        acceptNpc = true;
        ComponentId = currentComponent.Id;
        Status = CalculateQuestStatus(currentComponent);

        Logger.Debug($"[Quest] Start: character {Owner.Name}, do it - {TemplateId}, ComponentId {ComponentId}, Step {Step}, Status {Status}, res {res}, act.DetailType {acts[0].DetailType}");
        UseSkillAndBuff(currentComponent);
        SetNpcAggro(currentComponent);
    }
*/

    /// <summary>
    /// Use Skill на себя или на Npc, с которым взаимодействуем (Use Skill on yourself or on the Npc you interact with)
    /// </summary>
    /// <param name="component"></param>
    public void UseSkillAndBuff(QuestComponentTemplate component)
    {
        if (component == null) { return; }
        UseSkill(component);
        UseBuff(component);
    }

    private void UseBuff(QuestComponentTemplate component)
    {
        if (component.BuffId > 0)
        {
            Owner.Buffs.AddBuff(new Buff(Owner, Owner, SkillCaster.GetByType(SkillCasterType.Unit), _skillManager.GetBuffTemplate(component.BuffId), null, DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Use Skill defined in QuestComponent on yourself or on the Npc you interact with
    /// </summary>
    /// <param name="component"></param>
    private void UseSkill(QuestComponentTemplate component)
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

    /// <summary>
    /// If NpcAiId is AttackUnit, change the Npc faction to Monstrosity
    /// </summary>
    /// <param name="component"></param>
    public void SetNpcAggro(QuestComponentTemplate component)
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

    /// <summary>
    /// Distributes the items, xp and coins that are currently in the Rewards Pool and resets it
    /// Mails items if not enough space to add all items directly added to inventory
    /// </summary>
    /// <returns></returns>
    public bool DistributeRewards(bool addBaseQuestReward)
    {
        var res = true;
        // Distribute Items if needed
        if ((QuestRewardItemsPool.Count > 0) && (AllowItemRewards))
        {
            // TODO: Add a way to distribute honor or vocation badges in mail as well
            if (Owner.Inventory.Bag.FreeSlotCount < QuestRewardItemsPool.Count)
            {
                var mails = MailManager.CreateQuestRewardMails(Owner, this, QuestRewardItemsPool, QuestRewardCoinsPool);
                QuestRewardCoinsPool = 0; // Coins will be distributed in mail if any mail needed to be sent, so set to zero again
                foreach (var mail in mails)
                    if (!mail.Send())
                    {
                        Owner.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                        res = false;
                    }

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

        // Add quest level based rewards
        if ((addBaseQuestReward) && (Template.Level > 0) && (Step == QuestComponentKind.Reward))
        {
            var levelBasedRewards = QuestManager.Instance.GetSupplies(Template.Level);
            if (levelBasedRewards != null)
            {
                // Add (or reduce) extra for over-achieving (or early completing) of the quest if allowed
                QuestRewardRatio = Template.LetItDone ? GetQuestObjectivePercent() : 1f; // ratio is used by DistributeRewards
                QuestRewardExpPool += levelBasedRewards.Exp;
                QuestRewardCoinsPool += levelBasedRewards.Copper;
            }
        }

        // Add XP
        if (QuestRewardExpPool > 0)
        {
            var xp = (int)Math.Round(QuestRewardExpPool * QuestRewardRatio);
            if (xp > 0)
                Owner.AddExp(xp, true);
            QuestRewardExpPool = 0;
        }

        // Add copper coins
        if (QuestRewardCoinsPool > 0)
        {
            var copper = (int)Math.Round(QuestRewardCoinsPool * QuestRewardRatio);
            if (copper > 0)
                Owner.ChangeMoney(SlotType.None, SlotType.Inventory, copper);
            QuestRewardCoinsPool = 0;
        }

        // Cleanup used Items from quest
        if (QuestCleanupItemsPool.Count > 0)
        {
            foreach (var cleanupItem in QuestCleanupItemsPool)
                Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestComplete, cleanupItem.TemplateId, cleanupItem.Count, null);
            QuestCleanupItemsPool.Clear();
        }

        return res;
    }

    /// <summary>
    /// Drops the quest as a result of the player requesting it
    /// </summary>
    /// <param name="update">Should an update packet be sent to the player</param>
    public void Drop(bool update)
    {
        Status = QuestStatus.Dropped;
        Step = QuestComponentKind.Drop;

        for (var step = QuestComponentKind.Ready; step < QuestComponentKind.Reward; step++)
        {
            var component = Template.GetFirstComponent(step);
            if (component != null)
            {
                UseSkill(component);
                UseBuff(component);
            }
        }

        if (update)
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));

        foreach (var questComponentTemplate in Template.Components.Values)
            foreach (var actTemplate in questComponentTemplate.ActTemplates)
                actTemplate.QuestDropped(this);

        ClearObjectives();
    }

    /// <summary>
    /// Resets objectives
    /// </summary>
    private void ClearObjectives()
    {
        Objectives = new int[MaxObjectiveCount];
        RequestEvaluation();
    }

    /// <summary>
    /// Helper function for /quest GM command
    /// </summary>
    /// <param name="step"></param>
    /// <returns></returns>
    public int[] GetObjectives(QuestComponentKind step)
    {
        return Objectives;
    }

    /// <summary>
    /// Runs the QuestCleanup code of all the quest's acts
    /// </summary>
    public void Cleanup()
    {
        foreach (var questComponentTemplate in Template.Components.Values)
        {
            foreach (var actTemplate in questComponentTemplate.ActTemplates)
            {
                actTemplate.QuestCleanup(this);
            }
        }
    }

    /// <summary>
    /// Sets the RequestEvaluationFlag to true signalling the server that it should check this quest's progress again
    /// </summary>
    public void RequestEvaluation()
    {
        RequestEvaluationFlag = true;
    }

    /// <summary>
    /// Runs initializers for Acts that need to be activated at the start of the quest
    /// </summary>
    public void InitializeQuestActs()
    {
        foreach (var questStep in QuestSteps.Values)
        foreach (var questComponent in questStep.Components.Values)
        foreach (var questAct in questComponent.Acts)
            questAct.Template.InitializeQuest(this, questAct);
    }
    
    /// <summary>
    /// Runs Finalizers for Acts that are active the entire quest
    /// </summary>
    public void FinalizeQuestActs()
    {
        foreach (var questStep in QuestSteps.Values)
        foreach (var questComponent in questStep.Components.Values)
        foreach (var questAct in questComponent.Acts)
            questAct.Template.FinalizeQuest(this, questAct);
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
        stream.Write(LeftTime);       // quest time limit
        stream.Write(LeftTime == -1 ? 0 : ComponentId); // type(id) - indicates which step is limited
        stream.Write(DoodadId);                // doodadId
        stream.Write(DateTime.UtcNow);         // acceptTime
        stream.Write((byte)QuestAcceptorType); // type QuestAcceptorType
        stream.Write(AcceptorId);            // acceptorType npcId or doodadId
        return stream;
    }

    public void ReadData(byte[] data)
    {
        var stream = new PacketStream(data);

        // Read Objectives
        var newObjectives = new int[MaxObjectiveCount];
        for (var i = 0; i < MaxObjectiveCount; i++)
            newObjectives[i] = stream.ReadInt32();

        // Read Current Step
        Step = (QuestComponentKind)stream.ReadByte();

        // Reset objectives counts only after setting the step, or they will reset
        for (var i = 0; i < MaxObjectiveCount; i++)
            Objectives[i] = newObjectives[i];

        QuestAcceptorType = (QuestAcceptorType)stream.ReadByte();
        ComponentId = stream.ReadUInt32();
        AcceptorId = stream.ReadUInt32();
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
        stream.Write(AcceptorId);
        stream.Write(Time);
        return stream.GetBytes();
    }

    #endregion
}

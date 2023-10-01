using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.Quests;

// класс, определяющий состояние
public abstract class QuestState
{
    protected static Logger _log = LogManager.GetCurrentClassLogger();

    public Quest Quest { get; set; }
    public CurrentQuestComponent CurrentQuestComponent { get; set; }
    public List<QuestComponent> CurrentComponents { get; set; } = new();
    public List<QuestAct> CurrentActs { get; set; } = new();
    public QuestComponent CurrentComponent { get; set; }
    public QuestComponentKind Step { get; set; }
    public List<bool> ContextResults { get; set; }

    public abstract bool Start(bool forcibly = false);
    public abstract bool Update();
    public abstract bool Complete(int selected = 0);
    public abstract void Fail();
    public abstract void Drop();
    //public abstract List<bool> Execute();
    //public abstract void Handle(QuestContext context);

    public void UpdateContext(Quest quest, QuestState questState, QuestContext questContext, QuestComponentKind questComponentKind)
    {
        var exit = false;

        // необходимо проверить, какие шаги имеетюся
        for (var step = questComponentKind; step <= QuestComponentKind.Reward; step++)
        {
            var questComponents = quest.Template.GetComponents(step);
            if (questComponents.Length == 0) { break; }
            switch (step)
            {
                case QuestComponentKind.None:
                    {
                        quest.QuestNoneState = questContext;
                        quest.QuestNoneState.State = this;
                        quest.QuestNoneState.State.Quest = quest;
                        quest.QuestNoneState.State.Step = step;
                        quest.QuestNoneState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestNoneState.State.CurrentComponents = UpdateComponents();
                        quest.QuestNoneState.State.CurrentActs = UpdateActs();
                        quest.QuestNoneState.State.ContextResults = new List<bool>();
                        for (var i = 0; i < quest.QuestNoneState.State.CurrentComponents.Count; i++)
                        {
                            quest.QuestNoneState.State.ContextResults.Add(false);
                        }
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Start:
                    {
                        quest.QuestStartState = questContext;
                        quest.QuestStartState.State = this;
                        quest.QuestStartState.State.Quest = quest;
                        quest.QuestStartState.State.Step = step;
                        quest.QuestStartState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestStartState.State.CurrentComponents = UpdateComponents();
                        quest.QuestStartState.State.CurrentActs = UpdateActs();
                        quest.QuestStartState.State.ContextResults = new List<bool>();
                        for (var i = 0; i < quest.QuestStartState.State.CurrentComponents.Count; i++)
                        {
                            quest.QuestStartState.State.ContextResults.Add(false);
                        }
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Supply:
                    {
                        quest.QuestSupplyState = questContext;
                        quest.QuestSupplyState.State = this;
                        quest.QuestSupplyState.State.Quest = quest;
                        quest.QuestSupplyState.State.Step = step;
                        quest.QuestSupplyState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestSupplyState.State.CurrentComponents = UpdateComponents();
                        quest.QuestSupplyState.State.CurrentActs = UpdateActs();
                        quest.QuestSupplyState.State.ContextResults = new List<bool>();
                        for (var i = 0; i < quest.QuestSupplyState.State.CurrentComponents.Count; i++)
                        {
                            quest.QuestSupplyState.State.ContextResults.Add(false);
                        }
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Progress:
                    {
                        quest.QuestProgressState = questContext;
                        quest.QuestProgressState.State = this;
                        quest.QuestProgressState.State.Quest = quest;
                        quest.QuestProgressState.State.Step = step;
                        quest.QuestProgressState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestProgressState.State.CurrentComponents = UpdateComponents();
                        quest.QuestProgressState.State.CurrentActs = UpdateActs();
                        quest.QuestProgressState.State.ContextResults = new List<bool>();
                        for (var i = 0; i < quest.QuestProgressState.State.CurrentComponents.Count; i++)
                        {
                            quest.QuestProgressState.State.ContextResults.Add(false);
                        }
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Fail:
                    break;
                case QuestComponentKind.Ready:
                    {
                        quest.QuestReadyState = questContext;
                        quest.QuestReadyState.State = this;
                        quest.QuestReadyState.State.Quest = quest;
                        quest.QuestReadyState.State.Step = step;
                        quest.QuestReadyState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestReadyState.State.CurrentComponents = UpdateComponents();
                        quest.QuestReadyState.State.CurrentActs = UpdateActs();
                        quest.QuestReadyState.State.ContextResults = new List<bool>();
                        for (var i = 0; i < quest.QuestReadyState.State.CurrentComponents.Count; i++)
                        {
                            quest.QuestReadyState.State.ContextResults.Add(false);
                        }
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Drop:
                    break;
                case QuestComponentKind.Reward:
                    {
                        quest.QuestRewardState = questContext;
                        quest.QuestRewardState.State = this;
                        quest.QuestRewardState.State.Quest = quest;
                        quest.QuestRewardState.State.Step = step;
                        quest.QuestRewardState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestRewardState.State.CurrentComponents = UpdateComponents();
                        quest.QuestRewardState.State.CurrentActs = UpdateActs();
                        quest.QuestRewardState.State.ContextResults = new List<bool>();
                        for (var i = 0; i < quest.QuestRewardState.State.CurrentComponents.Count; i++)
                        {
                            quest.QuestRewardState.State.ContextResults.Add(false);
                        }
                        exit = true;
                        break;
                    }
            }
            if (exit) { break; }
        }

        return;

        CurrentQuestComponent UpdateComponent(QuestComponent[] components)
        {
            // собираем компоненты для шага квеста
            CurrentQuestComponent = new CurrentQuestComponent();
            foreach (var component in components)
            {
                CurrentQuestComponent.Add(component);
            }

            return CurrentQuestComponent;
        }
        List<QuestComponent> UpdateComponents()
        {
            CurrentComponents = CurrentQuestComponent.GetComponents();
            return CurrentComponents;
        }
        List<QuestAct> UpdateActs()
        {
            foreach (var component in CurrentComponents)
            {
                var acts = QuestManager.Instance.GetActs(component.Id);
                foreach (var act in acts)
                {
                    CurrentActs.Add((QuestAct)act);
                }
            }
            return CurrentActs;
        }
    }
}

// Конкретные классы, представляющие различные состояния
public class QuestNoneState : QuestState
{
    public override bool Start(bool forcibly = false)
    {
        _log.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId} начался!");

        var results = new List<bool>();
        if (forcibly)
        {
            results.Add(true);
        }
        else
        {
            results = CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
        }

        CurrentComponent = CurrentQuestComponent.GetFirstComponent();
        if (results.Any(b => b == true))
        {
            Quest.ComponentId = CurrentComponent.Id;

            if (Quest.QuestProgressState.State.CurrentQuestComponent != null)
            {
                // если есть шаг Progress, то не надо завершать квест
                Quest.Status = QuestStatus.Progress;
                Quest.Condition = QuestConditionObj.Progress;
            }
            else
            {
                Quest.Status = results.Any(b => b == true) ? QuestStatus.Ready : QuestStatus.Progress;
            }
            switch (Quest.Status)
            {
                case QuestStatus.Progress:
                case QuestStatus.Ready:
                    Quest.Condition = QuestConditionObj.Progress;
                    Quest.Step++; // Supply
                    break;
                default:
                    Quest.Step = QuestComponentKind.Fail;
                    Quest.Condition = QuestConditionObj.Fail;
                    break;
            }

            _log.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        }
        else
        {
            _log.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId} start failed.");
            _log.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            return false; // останавливаемся на этом шаге, сигнал на удаление квеста
        }
        Quest.UseSkillAndBuff(CurrentComponent);

        return true;
    }
    public override bool Update()
    {
        _log.Info($"[QuestNoneState][Update] Quest: {Quest.TemplateId} не может быть обновлен, пока не начался!");
        _log.Info($"[QuestNoneState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override bool Complete(int selected = 0)
    {
        _log.Info($"[QuestNoneState][Complete] Quest: {Quest.TemplateId} не может быть завершен, пока не начался!");
        _log.Info($"[QuestNoneState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override void Fail()
    {
        _log.Info($"[QuestNoneState][Fail] Quest: {Quest.TemplateId} не может завершиться неудачей, пока не начался!");
        _log.Info($"[QuestNoneState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        _log.Info($"[QuestNoneState][Drop] Квест {Quest.TemplateId} сброшен");
        _log.Info($"[QuestNoneState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    //public override List<bool> Execute()
    //{
    //    return CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
    //}
    //public override void Handle(QuestContext context)
    //{
    //    context.State = new QuestStartState(); // переключим на следующий контекст
    //    context.State.UpdateContext(Quest, context.State, context, QuestComponentKind.Start);
    //}
}
public class QuestStartState : QuestState
{
    public override bool Start(bool forcibly = false)
    {
        _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId} начался!");

        var results = new List<bool>();
        if (forcibly)
        {
            results.Add(true); // применяем квест насильно командой '/quest add <questId>', даже если нет рядом нужного Npc
        }
        else
        {
            results = CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
        }

        foreach (var component in CurrentComponents)
        {
            //var acts = QuestManager.Instance.GetActs(component.Id);
            //foreach (var act in acts)
            //{
            //    switch (act?.DetailType)
            //    {
            //        case "QuestActCheckTimer":
            //            {
            //                // TODO Timer - setting and starting time limit for the quest
            //                var template = act.GetTemplate<QuestActCheckTimer>();
            //                var res = act.Use(Quest.Owner, Quest, template.LimitTime);
            //                _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId} настройка и старт таймера ограничения времени на квест!");
            //                break;
            //            }
            //    }
            //}

            if (results.Any(b => b == true))
            {
                Quest.ComponentId = component.Id;

                if (Quest.QuestProgressState.State.CurrentQuestComponent != null)
                {
                    // если есть шаг Progress, то не надо завершать квест
                    Quest.Status = QuestStatus.Progress;
                    Quest.Condition = QuestConditionObj.Progress;

                    // проверим, что есть на этом шаге акт QuestActObjSphere
                    var progressContexts = Quest.QuestProgressState.State.CurrentQuestComponent.GetComponents();
                    foreach (var progressContext in progressContexts)
                    {
                        var actss = QuestManager.Instance.GetActs(progressContext.Id);
                        foreach (var act in actss)
                        {
                            switch (act?.DetailType)
                            {
                                case "QuestActObjSphere":
                                    {
                                        // подготовим работу QuestSphere
                                        // prepare QuestSphere's work
                                        _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}. Подписываемся на события, которые требуются для работы сферы");
                                        Quest.CurrentComponentId = progressContext.Id;
                                        var spheres = SphereQuestManager.Instance.GetQuestSpheres(progressContext.Id);
                                        if (spheres != null)
                                        {
                                            foreach (var sphere in spheres)
                                            {
                                                var sphereQuestTrigger = new SphereQuestTrigger();
                                                sphereQuestTrigger.Sphere = sphere;

                                                if (sphereQuestTrigger.Sphere == null)
                                                {
                                                    _log.Info($"[QuestStartState][Start] QuestActObjSphere: Sphere not found with cquest {CurrentComponent.Id} in quest_sign_spheres.json!");
                                                    break;
                                                }

                                                sphereQuestTrigger.Owner = Quest.Owner;
                                                sphereQuestTrigger.Quest = Quest;
                                                sphereQuestTrigger.TickRate = 500;

                                                SphereQuestManager.Instance.AddSphereQuestTrigger(sphereQuestTrigger);
                                            }

                                            const int Duration = 500;
                                            // TODO : Add a proper delay in here
                                            Task.Run(async () =>
                                            {
                                                await Task.Delay(Duration);
                                            });

                                            // подписка одна на всех
                                            Quest.Owner.Events.OnEnterSphere -= Quest.Owner.Quests.OnEnterSphereHandler;
                                            Quest.Owner.Events.OnEnterSphere += Quest.Owner.Quests.OnEnterSphereHandler;

                                            _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
                                            break;
                                        }

                                        // если сфера по какой-то причине отсутствует, будем считать, что мы её посетили
                                        // if the sphere is missing for some reason, we will assume that we have visited it
                                        Quest.Owner.SendMessage("[Quest] Quest {Quest.TemplateId}, Sphere not found - skipped..");
                                        _log.Info($"[QuestStartState][Start] Quest {Quest.TemplateId}, Sphere not found - skipped..");
                                        break;
                                    }
                            }
                        }
                    }
                }
                else
                {
                    Quest.Status = results.Any(b => b == true) ? QuestStatus.Ready : QuestStatus.Progress;
                }

                switch (Quest.Status)
                {
                    case QuestStatus.Progress:
                    case QuestStatus.Ready:
                        Quest.Condition = QuestConditionObj.Progress;
                        break;
                    default:
                        Quest.Step = QuestComponentKind.Fail;
                        Quest.Condition = QuestConditionObj.Fail;
                        break;
                }

                _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            }
            else
            {
                _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId} start failed.");
                _log.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
                return false; // останавливаемся на этом шаге, сигнал на удаление квеста
            }

            Quest.UseSkillAndBuff(CurrentComponent);
        }

        return true;
    }
    public override bool Update()
    {
        Quest.Status = QuestStatus.Ready;
        Quest.Condition = QuestConditionObj.Ready;
        Quest.Step = QuestComponentKind.Start;
        _log.Info($"[QuestStartState][Update] Quest: {Quest.TemplateId}. Ничего не делаем.");
        _log.Info($"[QuestStartState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override bool Complete(int selected = 0)
    {
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        _log.Info($"[QuestStartState][Complete] Quest: {Quest.TemplateId}. Ничего не делаем.");
        _log.Info($"[QuestStartState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override void Fail()
    {
        _log.Info($"[QuestStartState][Fail] Quest: {Quest.TemplateId} не может завершиться неудачей, пока не начался!");
        _log.Info($"[QuestStartState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        _log.Info($"[QuestStartState][Drop] Quest: {Quest.TemplateId} сброшен");
        _log.Info($"[QuestStartState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    //public override List<bool> Execute()
    //{
    //    return CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
    //}
    //public override void Handle(QuestContext context)
    //{
    //    context.State = new QuestSupplyState(); // переключим на следующий контекст
    //    context.State.UpdateContext(Quest, context.State, context, QuestComponentKind.Supply);
    //}
}
public class QuestSupplyState : QuestState
{
    public override bool Start(bool forcibly = false)
    {
        _log.Info($"[QuestSupplyState][Start] Quest: {Quest.TemplateId} в процессе выполнения.");
        _log.Info($"[QuestSupplyState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override bool Update()
    {
        _log.Info($"[QuestSupplyState][Update] Quest: {Quest.TemplateId}. Получим нужные предметы для прохождения квеста.");
        CurrentQuestComponent.Execute(Quest.Owner, Quest, 0); // получим квестовые предметы

        Quest.Status = QuestStatus.Ready;
        Quest.Condition = QuestConditionObj.Ready;
        _log.Info($"[QuestSupplyState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override bool Complete(int selected = 0)
    {
        _log.Info($"[QuestSupplyState][Complete] Quest: {Quest.TemplateId}. Ничего не делаем!");
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        Quest.Step = QuestComponentKind.Supply;
        _log.Info($"[QuestSupplyState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override void Fail()
    {
        _log.Info($"[QuestSupplyState][Fail] Quest: {Quest.TemplateId} провален");
        _log.Info($"[QuestSupplyState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        _log.Info($"[QuestSupplyState][Drop] Quest: {Quest.TemplateId} сброшен");
        _log.Info($"[QuestSupplyState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    //public override List<bool> Execute()
    //{
    //    return CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
    //}
    //public override void Handle(QuestContext context)
    //{
    //    context.State = new QuestProgressState(); // переключим на следующий контекст
    //    context.State.UpdateContext(Quest, context.State, context, QuestComponentKind.Progress);
    //}
}
public class QuestProgressState : QuestState
{
    public override bool Start(bool forcibly = false)
    {
        _log.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId} уже в процессе выполнения.");
        _log.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }

    //public int GetObjectiveCount(QuestAct act, clatt actDetailType)
    //{
    //    // нужно посмотреть в инвентарь, так как после Start() ещё не знаем, есть предмет в инвентаре или нет (we need to look in the inventory, because after Start() we don't know yet if the item is in the inventory or not)
    //    var objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);

    //    return 0;
    //}

    public override bool Update()
    {
        //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
        //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
        //var objectiveCount = Quest.GetObjectives(Step);

        ////var result = CurrentActs.Select(act => act.Use(Quest.Owner, Quest, 0)).ToList();
        //var results = CurrentQuestComponent.Execute(Quest.Owner, Quest, objectiveCount[0]);

        CurrentComponent = CurrentQuestComponent.GetFirstComponent();

        //if (results.All(b => b == true))
        //{
        //    _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}. Не надо подписываться на события.");
        //    Quest.ComponentId = CurrentComponent.Id;
        //    Quest.Status = QuestStatus.Ready;
        //    Quest.Condition = QuestConditionObj.Ready;
        //    _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        //    Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

        //    return true;
        //}

        _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}. Подписываемся на события, которые требуются для активных актов");
        bool res;
        var results2 = new List<bool>();

        foreach (var component in CurrentComponents)
        {
            var acts = QuestManager.Instance.GetActs(component.Id);

            foreach (var act in acts)
            {
                switch (act?.DetailType)
                {
                    case "QuestActObjMonsterHunt":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnMonsterHunt -= Quest.Owner.Quests.OnMonsterHuntHandler;
                            Quest.Owner.Events.OnMonsterHunt += Quest.Owner.Quests.OnMonsterHuntHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnMonsterHunt', Handler: 'OnMonsterHuntHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjMonsterGroupHunt":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnMonsterGroupHunt -= Quest.Owner.Quests.OnMonsterGroupHuntHandler;
                            Quest.Owner.Events.OnMonsterGroupHunt += Quest.Owner.Quests.OnMonsterGroupHuntHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnMonsterGroupHunt', Handler: 'OnMonsterGroupHuntHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemGather":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    _log.Info($"[QuestProgressState][Update][QuestActObjItemGroupGather] Quest: {Quest.TemplateId}. Подписываться на событие не надо, так как в инвентаре уже лежать нужные вещи.");
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnItemGather -= Quest.Owner.Quests.OnItemGatherHandler;
                            Quest.Owner.Events.OnItemGather += Quest.Owner.Quests.OnItemGatherHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnItemGather', Handler: 'OnItemGatherHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemGroupGather":
                        {
                            // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            res = CheckCount(act);
                            //result2 = act.Template.IsCompleted();
                            if (res)
                            {
                                _log.Info($"[QuestProgressState][Update][QuestActObjItemGroupGather] Quest: {Quest.TemplateId}. Подписываться на событие не надо, так как в инвентаре уже лежать нужные вещи.");
                                results2.Add(true); // уже выполнили задание, выход
                                break;
                            }

                            // подписка одна на всех
                            Quest.Owner.Events.OnItemGroupGather -= Quest.Owner.Quests.OnItemGroupGatherHandler;
                            Quest.Owner.Events.OnItemGroupGather += Quest.Owner.Quests.OnItemGroupGatherHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnItemGroupGather', Handler: 'OnItemGroupGatherHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemUse":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnItemUse -= Quest.Owner.Quests.OnItemUseHandler;
                            Quest.Owner.Events.OnItemUse += Quest.Owner.Quests.OnItemUseHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnItemUse', Handler: 'OnItemUseHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemGroupUse":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnItemGroupUse -= Quest.Owner.Quests.OnItemGroupUseHandler;
                            Quest.Owner.Events.OnItemGroupUse += Quest.Owner.Quests.OnItemGroupUseHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnItemGroupUse', Handler: 'OnItemGroupUseHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjInteraction":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnInteraction -= Quest.Owner.Quests.OnInteractionHandler;
                            Quest.Owner.Events.OnInteraction += Quest.Owner.Quests.OnInteractionHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnInteraction', Handler: 'OnInteractionHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjLevel":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnLevelUp -= Quest.Owner.Quests.OnLevelUpHandler;
                            Quest.Owner.Events.OnLevelUp += Quest.Owner.Quests.OnLevelUpHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnLevelUp', Handler: 'OnLevelUpHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjMateLevel":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjEffectFire":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjSendMail":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjZoneKill":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjZoneMonsterHunt":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjZoneNpcTalk":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjZoneQuestComplete":
                        {
                            //Quest.Owner.Events.OnMateLevel += Quest.Owner.Quests.OnMateLevelHandler;
                            //result2 = false;
                            break;
                        }
                    case "QuestActObjSphere":
                        {
                            // На шаге Start уже подписальсь на событие

                            //// подготовим работу QuestSphere
                            //// prepare QuestSphere's work
                            ////Quest.Status = QuestStatus.Progress;
                            Quest.CurrentComponentId = component.Id;
                            //var spheres = SphereQuestManager.Instance.GetQuestSpheres(CurrentComponent.Id);
                            //if (spheres != null)
                            //{
                            //    foreach (var sphere in spheres)
                            //    {
                            //        var sphereQuestTrigger = new SphereQuestTrigger();
                            //        sphereQuestTrigger.Sphere = sphere;

                            //        if (sphereQuestTrigger.Sphere == null)
                            //        {
                            //            _log.Info($"[Quest] QuestActObjSphere: Sphere not found with cquest {CurrentComponent.Id} in quest_sign_spheres.json!");
                            //            results2.Add(true); // уже выполнили задание, выход
                            //            break;
                            //        }

                            //        sphereQuestTrigger.Owner = Quest.Owner;
                            //        sphereQuestTrigger.Quest = Quest;
                            //        sphereQuestTrigger.TickRate = 500;

                            //        SphereQuestManager.Instance.AddSphereQuestTrigger(sphereQuestTrigger);
                            //    }

                            //    const int Duration = 500;
                            //    // TODO : Add a proper delay in here
                            //    Task.Run(async () =>
                            //    {
                            //        await Task.Delay(Duration);
                            //    });

                            // подписка одна на всех
                            Quest.Owner.Events.OnEnterSphere -= Quest.Owner.Quests.OnEnterSphereHandler;
                            Quest.Owner.Events.OnEnterSphere += Quest.Owner.Quests.OnEnterSphereHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
                            results2.Add(false); // будем ждать события
                            //    break;
                            //}

                            //// если сфера по какой-то причине отсутствует, будем считать, что мы её посетили
                            //// if the sphere is missing for some reason, we will assume that we have visited it
                            ////Quest.Status = QuestStatus.Progress;
                            //Quest.Owner.SendMessage($"[Quest] Quest {Quest.TemplateId}, Sphere not found - skipped..");
                            //_log.Info($"[QuestProgressState][Update] Quest {Quest.TemplateId}, Sphere not found - skipped..");
                            //results2.Add(true); // не будем ждать события
                            break;
                        }
                    case "QuestActObjTalk":
                        {
                            // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            res = CheckCount(act);
                            //result2 = act.Template.IsCompleted();
                            if (res)
                            {
                                results2.Add(true); // уже выполнили задание, выход
                                break;
                            }

                            // подписка одна на всех
                            Quest.Owner.Events.OnTalkMade -= Quest.Owner.Quests.OnTalkMadeHandler;
                            Quest.Owner.Events.OnTalkMade += Quest.Owner.Quests.OnTalkMadeHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnTalkMade', Handler: 'OnTalkMadeHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjTalkNpcGroup":
                        {
                            // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            res = CheckCount(act);
                            //result2 = act.Template.IsCompleted();
                            if (res)
                            {
                                results2.Add(true); // уже выполнили задание, выход
                                break;
                            }

                            // подписка одна на всех
                            Quest.Owner.Events.OnTalkNpcGroupMade -= Quest.Owner.Quests.OnTalkNpcGroupMadeHandler;
                            Quest.Owner.Events.OnTalkNpcGroupMade += Quest.Owner.Quests.OnTalkNpcGroupMadeHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnTalkNpcGroupMade', Handler: 'OnTalkNpcGroupMadeHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjExpressFire":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnExpressFire -= Quest.Owner.Quests.OnExpressFireHandler;
                            Quest.Owner.Events.OnExpressFire += Quest.Owner.Quests.OnExpressFireHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnExpressFire', Handler: 'OnExpressFireHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjAggro":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnAggro -= Quest.Owner.Quests.OnAggroHandler;
                            Quest.Owner.Events.OnAggro += Quest.Owner.Quests.OnAggroHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnAggro', Handler: 'OnAggroHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjAbilityLevel":
                        {
                            //// нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            //// we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnAbilityLevelUp -= Quest.Owner.Quests.OnAbilityLevelUpHandler;
                            Quest.Owner.Events.OnAbilityLevelUp += Quest.Owner.Quests.OnAbilityLevelUpHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnAbilityLevelUp', Handler: 'OnAbilityLevelUpHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjCraft":
                        {
                            // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            res = CheckCount(act);
                            //result2 = act.Template.IsCompleted();
                            if (res)
                            {
                                results2.Add(true); // уже выполнили задание, выход
                                break;
                            }

                            // подписка одна на всех
                            Quest.Owner.Events.OnCraft -= Quest.Owner.Quests.OnCraftHandler;
                            Quest.Owner.Events.OnCraft += Quest.Owner.Quests.OnCraftHandler;

                            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Event: 'OnCraft', Handler: 'OnCraftHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjCompleteQuest":
                        {
                            //Quest.Owner.Events.OnItemGather += Quest.Owner.Quests.OnItemGatherHandler;
                            //result2 = false;
                            break;
                        }
                }
            }
        }

        if (results2.All(b => b == true))
        {
            Quest.ComponentId = CurrentComponent.Id;
            Quest.Status = QuestStatus.Ready;
            Quest.Condition = QuestConditionObj.Ready;
            _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

            // нужен здесь
            Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

            return true;
        }

        Quest.ComponentId = 0;
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        _log.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        // не нужен здесь
        //Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

        return false;
    }

    private bool CheckCount(IQuestAct act)
    {
        // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
        // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
        var objectiveCount = 0;
        switch (act.DetailType)
        {
            case "QuestActObjMonsterHunt":
                {
                    //var template = act.GetTemplate<QuestActObjMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                    //objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.NpcId);
                    break;
                }
            case "QuestActObjItemGather":
                {
                    var template = act.GetTemplate<QuestActObjItemGather>(); // для доступа к переменным требуется привидение к нужному типу
                    objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                    break;
                }
            case "QuestActObjItemUse":
                {
                    //var template = act.GetTemplate<QuestActObjItemUse>(); // для доступа к переменным требуется привидение к нужному типу
                    //objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                    break;
                }
            case "QuestActObjTalk":
                {
                    var template = act.GetTemplate<QuestActObjTalk>(); // для доступа к переменным требуется привидение к нужному типу
                    objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                    break;
                }
            case "QuestActObjCraft":
                {
                    var template = act.GetTemplate<QuestActObjCraft>(); // для доступа к переменным требуется привидение к нужному типу
                    objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.CraftId);
                    break;
                }
        }

        //var objective = act.Template.GetCount();
        var result = CurrentQuestComponent.Execute(Quest.Owner, Quest, objectiveCount).Any(b => b == true);
        return result;
    }

    public override bool Complete(int selected = 0)
    {
        _log.Info($"[QuestProgressState][Complete] Quest: {Quest.TemplateId}. Шаг успешно завершен!");
        //Quest.Step++; // переход к следующему шагу
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        Quest.Step = QuestComponentKind.Progress;
        _log.Info($"[QuestProgressState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override void Fail()
    {
        _log.Info($"[QuestProgressState][Fail] Quest: {Quest.TemplateId} провален");
        _log.Info($"[QuestProgressState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        _log.Info($"[QuestProgressState][Drop] Quest: {Quest.TemplateId} сброшен");
        _log.Info($"[QuestProgressState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    //public override List<bool> Execute()
    //{
    //    return CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
    //}
    //public override void Handle(QuestContext context)
    //{
    //    context.State = new QuestReadyState(); // переключим на следующий контекст
    //    context.State.UpdateContext(Quest, context.State, context, QuestComponentKind.Ready);
    //}
}
public class QuestReadyState : QuestState
{
    public override bool Start(bool forcibly = false)
    {
        _log.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId} уже в процессе выполнения.");
        _log.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override bool Update()
    {
        //var results = CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);

        CurrentComponent = CurrentQuestComponent.GetFirstComponent();

        //if (results.All(b => b == true))
        //{
        //    _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}. Не надо подписываться на события.");
        //    Quest.ComponentId = CurrentComponent.Id;
        //    Quest.Status = QuestStatus.Ready;
        //    Quest.Condition = QuestConditionObj.Ready;
        //    _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        //    Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

        //    return true;
        //}

        _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}. Подписываемся на события, которые требуются для активных актов");
        bool res;
        var results2 = new List<bool>();

        foreach (var component in CurrentComponents)
        {
            var acts = QuestManager.Instance.GetActs(component.Id);

            foreach (var act in acts)
            {
                switch (act?.DetailType)
                {
                    case "QuestActConReportNpc":
                        {
                            Quest.Owner.Events.OnReportNpc += Quest.Owner.Quests.OnReportNpcHandler;
                            _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActConReportDoodad":
                        {
                            Quest.Owner.Events.OnReportDoodad += Quest.Owner.Quests.OnReportDoodadHandler;
                            _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Event: 'OnReportDoodad', Handler: 'OnReportDoodadHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActConReportJournal":
                        {
                            //Quest.Owner.Events.OnReportJournal += Quest.Owner.Quests.OnReportJournalHandler;
                            _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Event: 'OnReportJournal', Handler: 'OnReportJournalHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActConAutoComplete":
                        {
                            //Quest.Owner.Events.OnQuestComplete += Quest.Owner.Quests.OnQuestCompleteHandler;
                            _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Event: 'OnQuestComplete', Handle: 'OnEventsOnQuestComplete'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                }
            }
        }

        if (results2.All(b => b == true))
        {
            Quest.ComponentId = CurrentComponent.Id;
            Quest.Status = QuestStatus.Ready;
            Quest.Condition = QuestConditionObj.Ready;
            _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

            // не нужен здесь?
            Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

            return true;
        }

        Quest.ComponentId = 0;
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        _log.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        // не нужен здесь
        //Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

        return false;
    }
    public override bool Complete(int selected = 0)
    {
        //Quest.Step++; // переход к следующему шагу
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        Quest.Step = QuestComponentKind.Ready;
        _log.Info($"[QuestReadyState][Complete] Quest: {Quest.TemplateId}. Шаг успешно завершен!");
        _log.Info($"[QuestReadyState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override void Fail()
    {
        _log.Info($"[QuestReadyState][Fail] Квест {Quest.TemplateId} провален");
        _log.Info($"[QuestReadyState][Fail] Квест {Quest.TemplateId} Подписываемся на события, которые требуются для активных актов");
        foreach (var act in CurrentActs)
        {
            switch (act?.DetailType)
            {
                case "QuestActConFail":
                    //Quest.Owner.Events.OnFail += Quest.OnFailHandler;
                    break;
            }
        }
    }
    public override void Drop()
    {
        _log.Info($"[QuestReadyState][Drop] Квест {Quest.TemplateId} сброшен");
        _log.Info($"[QuestReadyState][Drop] Квест {Quest.TemplateId} уже завершен. Нельзя провалить.");
    }
    //public override List<bool> Execute()
    //{
    //    return CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
    //}
    //public override void Handle(QuestContext context)
    //{
    //    context.State = new QuestRewardState(); // переключим на следующий контекст
    //    context.State.UpdateContext(Quest, context.State, context, QuestComponentKind.Reward);
    //}
}
public class QuestRewardState : QuestState
{
    public override bool Start(bool forcibly = false)
    {
        _log.Info($"[QuestRewardState][Start] Quest: {Quest.TemplateId}. Уже в процессе выполнения.");
        _log.Info($"[QuestRewardState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override bool Update()
    {
        _log.Info($"[QuestRewardState][Update] Quest: {Quest.TemplateId}. Получаем бонусы.");

        // получение бонусов организовано в Quests.Complete
        // на этом шаге может быть Act Supply
        //var result = CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);

        Quest.Status = QuestStatus.Ready;
        Quest.Condition = QuestConditionObj.Ready;
        _log.Info($"[QuestRewardState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        // не нужен здесь
        //Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));

        return true;
    }
    public override bool Complete(int selected = 0)
    {
        _log.Info($"[QuestRewardState][Complete] Quest: {Quest.TemplateId}. Завершаем квест.");
        _log.Info($"[QuestRewardState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        Quest.Owner.Quests.Complete(Quest.TemplateId, selected); // Завершаем квест
        Quest.Condition = QuestConditionObj.Complete;
        Quest.Step = QuestComponentKind.Reward;

        return true;
    }
    public override void Fail()
    {
        _log.Info($"[QuestRewardState][Fail] Quest: {Quest.TemplateId} уже завершен. Нельзя провалить.");
        _log.Info($"[QuestRewardState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        _log.Info($"[QuestRewardState][Drop] Quest: {Quest.TemplateId} уже завершен. Нельзя сбросить.");
        _log.Info($"[QuestRewardState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    //public override List<bool> Execute()
    //{
    //    return CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
    //}
    //public override void Handle(QuestContext context)
    //{
    //    // TODO какой должен быть шаг?
    //    context.State = new QuestRewardState(); // переключим на следующий контекст
    //    context.State.UpdateContext(Quest, context.State, context, QuestComponentKind.Reward);
    //}
}

using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Quests;

public partial class Quest : PacketMarshaler
{
    private object _lock = new();
    public QuestContext QuestNoneState { get; set; }
    public QuestContext QuestStartState { get; set; }
    public QuestContext QuestSupplyState { get; set; }
    public QuestContext QuestProgressState { get; set; }
    public QuestContext QuestReadyState { get; set; }
    public QuestContext QuestRewardState { get; set; }
    public Dictionary<uint, bool> isInProgress { get; set; }
    // TODO здесь новый код для квестов

    public void SetInProgress(uint questId, bool value)
    {
        if (isInProgress.ContainsKey(questId))
        {
            isInProgress[questId] = value;
        }
        else
        {
            isInProgress.TryAdd(questId, value);
        }
    }
    public bool CheckInProgress(uint questId)
    {
        isInProgress.TryGetValue(questId, out var result);
        return result;
    }

    /// <summary>
    /// инициализируем все шаги нужные квесту
    /// </summary>
    public void CreateContextInstance()
    {
        QuestNoneState = new QuestContext(this, new QuestNoneState(), QuestComponentKind.None);
        QuestStartState = new QuestContext(this, new QuestStartState(), QuestComponentKind.Start);
        QuestSupplyState = new QuestContext(this, new QuestSupplyState(), QuestComponentKind.Supply);
        QuestProgressState = new QuestContext(this, new QuestProgressState(), QuestComponentKind.Progress);
        QuestReadyState = new QuestContext(this, new QuestReadyState(), QuestComponentKind.Ready);
        QuestRewardState = new QuestContext(this, new QuestRewardState(), QuestComponentKind.Reward);
        isInProgress = new Dictionary<uint, bool>();
    }

    /// <summary>
    /// Запуск начального шага квеста
    /// </summary>
    /// <returns></returns>
    public bool StartQuest(bool forcibly = false)
    {
        // шаг None или Start
        if (QuestNoneState?.State?.CurrentQuestComponent != null)
        {
            if (Status == QuestStatus.Invalid)
            {
                if (!QuestNoneState.State.Start(forcibly)) { return false; } // сбросим принятый квест
            }
            else
            {
                return false;
            }

            Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));

            return true; // принимаем квест
        }
        if (QuestStartState?.State?.CurrentQuestComponent != null)
        {
            Step = QuestComponentKind.Start;
            if (Status == QuestStatus.Invalid)
            {
                if (!QuestStartState.State.Start(forcibly)) { return false; } // сбросим принятый квест
            }
            else
            {
                return false;
            }

            Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));

            return true; // принимаем квест
        }

        Logger.Info($"Quest Start: шага 'None' или'Start' нет в квесте {Id}");
        return false; // не принимаем квест
    }

    /// <summary>
    /// Контекстная обработка квеста - перебираем существующие шаги и выполняем их
    /// step - указывает на то, какий именно шаг в данный момент текущий
    /// status - для квеста в игре: progress - 'in-prog.', ready - 'complete', complete - получение бонусов
    /// status - какой метод будет выполняться    progress -         , progress -         , ready -           , complete -
    /// plus                                               - update()           - update()        - complete()           - всё, получение бонусов
    /// condition - какой метод будет выполняться progress -         , ready    -,          ready -           , complete -
    /// </summary>
    /// <summary>
    /// Обработка контекста
    /// </summary>
    public void ContextProcessing(int selected = 0)
    {
        Logger.Info($"[ContextProcessing] квест {TemplateId}.");
        var next = true;
        while (next)
        {
            Logger.Info($"[ContextProcessing][while] квест {TemplateId}.");
            switch (Step)
            {
                case QuestComponentKind.Supply when QuestSupplyState?.State?.CurrentQuestComponent != null:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestSupplyState][Update] квест {TemplateId}.");
                            QuestSupplyState.State.Update();
                            Condition = QuestConditionObj.Ready;
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestSupplyState][Complete] квест {TemplateId}.");
                            QuestSupplyState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            Step++; // переход к следующему шагу
                            break;
                    }

                    break;
                case QuestComponentKind.Progress when QuestProgressState?.State?.CurrentQuestComponent != null:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestProgressState][Update] квест {TemplateId}.");
                            if (!QuestProgressState.State.Update())
                            {
                                next = false;
                                Condition = QuestConditionObj.Progress;
                                break;
                            } // подписка на события и прерываем цикл

                            Condition = QuestConditionObj.Ready;
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestProgressState][Complete] квест {TemplateId}.");
                            QuestProgressState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            Step++; // переход к следующему шагу
                            break;
                    }

                    break;
                case QuestComponentKind.Ready when QuestReadyState?.State?.CurrentQuestComponent != null:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestReadyState][Update] квест {TemplateId}.");
                            if (!QuestReadyState.State.Update())
                            {
                                next = false;
                                Condition = QuestConditionObj.Progress;
                                break;
                            } // подписка на события и прерываем цикл

                            Condition = QuestConditionObj.Ready;
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestReadyState][Complete] квест {TemplateId}.");
                            QuestReadyState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            Step++; // переход к следующему шагу
                            break;
                    }

                    break;
                case QuestComponentKind.Reward when QuestRewardState?.State?.CurrentQuestComponent != null:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestRewardState][Update] квест {TemplateId}.");
                            QuestRewardState.State.Update();
                            Condition = QuestConditionObj.Ready;
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestRewardState][Complete] квест {TemplateId}.");
                            QuestRewardState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            next = false; // прерываем цикл
                            break;
                    }

                    break;
                case QuestComponentKind.None: // TODO вроде бы есть квесты с этим шагом
                case QuestComponentKind.Start:
                case QuestComponentKind.Fail:
                case QuestComponentKind.Drop:
                default:
                    Step++; // переход к следующему шагу
                    break;
            }
        }
    }

    public void RecallEvents()
    {
        switch (Step)
        {
            case QuestComponentKind.Progress when QuestProgressState?.State?.CurrentQuestComponent != null:
                QuestProgressState.State.Update();
                Step = QuestComponentKind.Progress; // обновим
                Status = QuestStatus.Progress;
                Condition = QuestConditionObj.Progress;
                Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));

                break;
            case QuestComponentKind.Ready when QuestReadyState?.State?.CurrentQuestComponent != null:
                QuestReadyState.State.Update();
                Step = QuestComponentKind.Ready; // обновим
                Status = QuestStatus.Ready;
                Condition = QuestConditionObj.Ready;
                Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));

                break;
        }
    }

    private bool GetQuestContext(string actDetailType, out QuestContext context, out List<QuestAct> acts)
    {
        context = new QuestContext(this, new QuestProgressState(), QuestComponentKind.Progress);
        acts = new List<QuestAct>();
        var next = true;
        const int maxDepth = 5;
        var recurse = 0;
        while (next)
        {
            switch (Step)
            {
                case QuestComponentKind.Progress when QuestProgressState.State.CurrentQuestComponent != null:
                    context = QuestProgressState; // шаг Progress
                    acts = QuestProgressState.State.CurrentActs;
                    next = false;
                    foreach (var act in acts)
                    {
                        if (act.DetailType != actDetailType)
                        {
                            next = true;
                            recurse++;
                            continue;
                        }

                        next = false;
                        break;
                    }

                    if (recurse > maxDepth)
                    {
                        next = false;
                    }
                    break;
                case QuestComponentKind.Ready when QuestReadyState.State.CurrentQuestComponent != null:
                    context = QuestReadyState; // шаг Ready
                    acts = QuestReadyState.State.CurrentActs;
                    next = false;
                    foreach (var act in acts)
                    {
                        if (act.DetailType != actDetailType)
                        {
                            next = true;
                            recurse++;
                            continue;
                        }

                        next = false;
                        break;
                    }

                    if (recurse > maxDepth)
                    {
                        next = false;
                    }
                    break;
                case QuestComponentKind.Reward when QuestRewardState.State.CurrentQuestComponent != null:
                    context = QuestRewardState; // шаг Reward
                    acts = QuestRewardState.State.CurrentActs;
                    next = false;
                    foreach (var act in acts)
                    {
                        if (act.DetailType != actDetailType)
                        {
                            next = true;
                            recurse++;
                            continue;
                        }

                        next = false;
                        break;
                    }

                    if (recurse > maxDepth)
                    {
                        next = false;
                    }
                    break;
                default:
                    Step++;
                    break;
            }
        }

        return context == null;
    }

    #region Events

    #region Progress step

    // Внимание!!!
    // выполняются на шаге Progress
    // для этих событий не будет известен QuestId и будет перебор всех активных квестов
    // что-бы по два раза не вызывались надо перед подпиской на событие отписываться!!!
    public void OnInteractionHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 3353, 1213, 1218
        // OnInteraction - похож на OnTalkMadeHandler
        var args = eventArgs as OnInteractionArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjInteraction", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }
        var componentIndex = 0;

        foreach (var component in context.State.CurrentComponents)
        {
            complete = false;
            ComponentId = component.Id;
            var actss = _questManager.GetActs(component.Id);
            foreach (var act in actss)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjInteraction")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, взаимодействие с Doodad...");

                var template = act.GetTemplate<QuestActObjInteraction>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Npc, может быть не тот, что надо по квесту
                if (template?.DoodadId != args.DoodadId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true))
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || OverCompletionPercent >= Template.Score && Template.Score != 0
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnInteractionHandler] Отписываемся от события.");
            Logger.Info($"[OnInteractionHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, Event: 'OnInteraction', Handler: 'OnInteractionHandler'");
            Owner.Events.OnInteraction -= Owner.Quests.OnInteractionHandler; // отписываемся
            Owner.Events.OnInteraction += Owner.Quests.OnInteractionHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnInteractionHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnInteractionHandler] Подписываемся на событие.");
            Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnInteractionHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 250, 1573, 4289
        var args = eventArgs as OnMonsterHuntArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjMonsterHunt", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }
        var componentIndex = 0;

        foreach (var component in context.State.CurrentComponents)
        {
            complete = false;
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjMonsterHunt")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Kill event triggered");

                var template = act.GetTemplate<QuestActObjMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;

                Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true))
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || selective
            || OverCompletionPercent >= Template.Score && Template.Score != 0
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnMonsterHuntHandler] Отписываемся от события.");
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterHunt', Handler: 'OnMonsterHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterHuntHandler; // отписываемся
            Owner.Events.OnMonsterHunt += Owner.Quests.OnMonsterHuntHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnMonsterHuntHandler] Подписываемся на событие.");
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 266, 1233, 4295
        var args = eventArgs as OnMonsterGroupHuntArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjMonsterGroupHunt", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }
        var componentIndex = 0;

        foreach (var component in context.State.CurrentComponents)
        {
            complete = false;
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjMonsterGroupHunt")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Kill event triggered");

                var template = act.GetTemplate<QuestActObjMonsterGroupHunt>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                if (!_questManager.CheckGroupNpc(template.QuestMonsterGroupId, args.NpcId))
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность 4295
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true))
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || selective
            || OverCompletionPercent >= Template.Score && Template.Score != 0
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnMonsterGroupHuntHandler] Отписываемся от события.");
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterGroupHunt', Handler: 'OnMonsterGroupHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterGroupHuntHandler; // отписываемся
            Owner.Events.OnMonsterHunt += Owner.Quests.OnMonsterGroupHuntHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnMonsterGroupHuntHandler] Подписываемся на событие.");
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemUseHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 252, 1222
        var args = eventArgs as OnItemUseArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjItemUse", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjItemUse")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, использовали предмет из инвентаря...");

                var template = act.GetTemplate<QuestActObjItemUse>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там использовали, может быть не то, что надо по квесту
                if (template?.ItemId != args.ItemId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true))
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || selective
            || OverCompletionPercent >= Template.Score && Template.Score != 0
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnItemUseHandler] Отписываемся от события.");
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Event: 'OnItemUse', Handler: 'OnItemUseHandler'");
            Owner.Events.OnItemUse -= Owner.Quests.OnItemUseHandler; // отписываемся
            Owner.Events.OnItemUse += Owner.Quests.OnItemUseHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnItemUseHandler] Подписываемся на событие.");
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemGroupUseHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnItemGroupUseArgs;
        if (args == null)
            return;

        //var //Step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjItemGroupUse", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjItemGroupUse")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, использовали предмет из инвентаря...");

                var template = act.GetTemplate<QuestActObjItemGroupUse>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там использовали, может быть не то, что надо по квесту
                if (!_questManager.CheckGroupItem(template.ItemGroupId, args.ItemGroupId))
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true))
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || selective
            || OverCompletionPercent >= Template.Score && Template.Score != 0
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnItemGroupUseHandler] Отписываемся от события.");
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Event: 'OnItemGroupUse', Handler: 'OnItemGroupUseHandler'");
            Owner.Events.OnItemGroupUse -= Owner.Quests.OnItemGroupUseHandler; // отписываемся
            Owner.Events.OnItemGroupUse += Owner.Quests.OnItemGroupUseHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnItemGroupUseHandler] Подписываемся на событие.");
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 251, 324, 953, 1215, 1216, 1233, 2300
        var args = eventArgs as OnItemGatherArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjItemGather", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            complete = false;
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjItemGather")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, в инвентарь добавился предмет {args.ItemId}.");

                var template = act.GetTemplate<QuestActObjItemGather>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                if (template?.ItemId != args.ItemId)
                {
                    Logger.Info($"[OnItemGatherHandler] Это предмет {args.ItemId} не тот, что нужен нам {template?.ItemId}. Квест {TemplateId}.");
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                var objectiveCount = Owner.Inventory.GetItemsCount(args.ItemId);
                Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, заглянули в инвентарь, есть такой предмет {args.ItemId} в количестве {objectiveCount}.");

                // увеличиваем objective
                Objectives[componentIndex] = objectiveCount;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true) && !Template.LetItDone)
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
                Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, выполнен этап {componentIndex}, пробуем запустим скилл и/или баф.");
            }

            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || selective
            || OverCompletionPercent >= Template.Score && Template.Score != 0 // для квеста 1135
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnItemGatherHandler] Отписываемся от события.");
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Event: 'OnItemGather', Handler: 'OnItemGatherHandler'");
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Owner.Events.OnItemGather -= Owner.Quests.OnItemGatherHandler; // отписываемся
            Owner.Events.OnItemGather += Owner.Quests.OnItemGatherHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;

            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnItemGatherHandler] Подписываемся на событие.");
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemGroupGatherHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnItemGroupGatherArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjItemGroupGather", out var context, out var listQuestActs))
            return;
        //Step = step;

        var selective = false;
        var complete = false;
        var results = false;
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjItemGroupGather")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, в инвентарь добавился предмет.");

                var template = act.GetTemplate<QuestActObjItemGroupGather>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                if (!_questManager.CheckGroupItem(template.ItemGroupId, args.ItemId))
                {
                    Logger.Info($"[OnItemGroupGatherHandler] Это не тот предмет {template.ItemGroupId}, что нужен нам {args.ItemId}. Квест {TemplateId}.");
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }
                Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, в инвентаре есть нужный предмет {args.ItemId} в количестве {args.Count}.");

                // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                var objectiveCount = Owner.Inventory.GetItemsCount(args.ItemId);

                // увеличиваем objective
                Objectives[componentIndex] = objectiveCount;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");

                // проверка результатов на валидность
                // Validation of context.State.ContextResults
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    if (context.State.ContextResults.Any(b => b == true)) { selective = true; }

                    Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, позволяет сделать выбор Selective {Template.Selective}.");
                }
                else if (context.State.ContextResults.All(b => b == true))
                {
                    // состоит из одного компонента и он выполнен
                    results = complete;
                    Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, выполнены все этапы с результатом {complete}.");
                }
                else if (Template.Score == 0 && componentIndex == context.State.CurrentComponents.Count - 1 && context.State.CurrentComponents.Count > 1)
                {
                    // выполнен последний компонент из нескольких
                    results = complete;
                    Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, выполнен последний {componentIndex} этап с результатом {complete}.");
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results
            || selective
            || OverCompletionPercent >= Template.Score && Template.Score != 0
            && !(EarlyCompletion || ExtraCompletion)
           )
        {
            Logger.Info($"[OnItemGroupGatherHandler] Отписываемся от события.");
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Event: 'OnItemGroupGather', Handler: 'OnItemGroupGatherHandler'");
            Owner.Events.OnItemGroupGather -= Owner.Quests.OnItemGroupGatherHandler; // отписываемся
            Owner.Events.OnItemGroupGather += Owner.Quests.OnItemGroupGatherHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        if (EarlyCompletion || ExtraCompletion)
        {
            Logger.Info($"[OnItemGroupGatherHandler] Подписываемся на событие.");
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
        }

        // проверка результатов на валидность, 266 - GroupHunt & ItemGather
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        //Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnAggroArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjAggro", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        context.State.ContextResults = new List<bool>();
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjAggro")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, кто-то аггрится...");

                var template = act.GetTemplate<QuestActObjAggro>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (MathUtil.CalculateDistance(Owner.Transform.World.Position, args.Transform.World.Position) > template.Range)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnAggroHandler] Отписываемся от события.");
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, Event: 'OnAggro', Handler: 'OnAggroHandler'");
            Owner.Events.OnAggro -= Owner.Quests.OnAggroHandler; // отписываемся
            Owner.Events.OnAggro += Owner.Quests.OnAggroHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        // Quest: .
        var args = eventArgs as OnExpressFireArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjExpressFire", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjExpressFire")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Express Fire...");

                var template = act.GetTemplate<QuestActObjExpressFire>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (template.ExpressKeyId != args.EmotionId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnExpressFireHandler] Отписываемся от события.");
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Event: 'OnExpressFire', Handler: 'OnExpressFireHandler'");
            Owner.Events.OnExpressFire -= Owner.Quests.OnExpressFireHandler; // отписываемся
            Owner.Events.OnExpressFire += Owner.Quests.OnExpressFireHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 5967
        var args = eventArgs as OnAbilityLevelUpArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjAbilityLevel", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjAbilityLevel")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Ability Level Up...");

                //var template = act.GetTemplate<QuestActObjAbilityLevel>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                //if (template.Level < Owner.Level)
                //{
                //    ThisIsNotWhatYouNeed[componentIndex] = true;
                //    continue;
                //}

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnAbilityLevelUpHandler] Отписываемся от события.");
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Event: 'OnExpressFire', Handler: 'OnAbilityLevelUpHandler'");
            Owner.Events.OnAbilityLevelUp -= Owner.Quests.OnAbilityLevelUpHandler; // отписываемся
            Owner.Events.OnAbilityLevelUp += Owner.Quests.OnAbilityLevelUpHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnLevelUpArgs;
        if (args == null)
            throw new NotImplementedException();

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjLevel", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjLevel")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Level Up...");

                var template = act.GetTemplate<QuestActObjLevel>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (template.Level >= Owner.Level)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnLevelUpHandler] Отписываемся от события.");
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Event: 'OnLevelUp', Handler: 'OnLevelUpHandler'");
            Owner.Events.OnLevelUp -= Owner.Quests.OnLevelUpHandler; // отписываемся
            Owner.Events.OnLevelUp += Owner.Quests.OnLevelUpHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 6024
        var args = eventArgs as OnCraftArgs;
        if (args == null)
            throw new NotImplementedException();

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjCraft", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjCraft")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Level Up...");

                var template = act.GetTemplate<QuestActObjCraft>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (template.CraftId != args.CraftId/* && Objectives[componentIndex] > template.Count*/)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnCraftHandler] Отписываемся от события.");
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Event: 'OnCraft', Handler: 'OnCraftHandler'");
            Owner.Events.OnCraft -= Owner.Quests.OnCraftHandler; // отписываемся
            Owner.Events.OnCraft += Owner.Quests.OnCraftHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2762, 6024
        var args = eventArgs as OnEnterSphereArgs;
        if (args == null)
            throw new NotImplementedException();

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Progress;
        if (GetQuestContext("QuestActObjSphere", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjSphere")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Enter Sphere...");

                var template = act.GetTemplate<QuestActObjSphere>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (component.Id != args.SphereQuest.ComponentID)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnEnterSphereHandler] Отписываемся от события.");
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
            Owner.Events.OnEnterSphere -= Owner.Quests.OnEnterSphereHandler; // отписываемся
            Owner.Events.OnEnterSphere += Owner.Quests.OnEnterSphereHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }

    #endregion Progress step

    #region Ready step

    // Внимание!!!
    // для этого события будет известен QuestId
    // выполняется на шаге Ready
    public void OnTalkMadeHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2037
        // OnInteraction - похож на OnTalkMadeHandler
        var args = eventArgs as OnTalkMadeArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Ready;
        if (GetQuestContext("QuestActObjTalk", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjTalk")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, взаимодействие с Npc...");

                var template = act.GetTemplate<QuestActObjTalk>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Npc, может быть не тот, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;

                // запускаем AI для Npc
                switch (component.NpcAiId)
                {
                    case QuestNpcAiName.FollowPath:
                        {
                            var route = component.AiPathName;
                            var npcs = WorldManager.Instance.GetAllNpcs();
                            foreach (var npc in npcs)
                            {
                                if (npc.TemplateId != template.NpcId) { continue; }

                                if (npc.IsInPatrol) { break; }

                                switch (component.AiPathTypeId)
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
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnTalkMadeHandler] Отписываемся от события.");
            Logger.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, Event: 'OnTalkMade', Handler: 'OnTalkMadeHandler'");
            Owner.Events.OnTalkMade -= Owner.Quests.OnTalkMadeHandler; // отписываемся
            //Owner.Events.OnTalkMade += Owner.Quests.OnTalkMadeHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnTalkNpcGroupMadeHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnTalkNpcGroupMadeArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        //Step = QuestComponentKind.Ready;
        if (GetQuestContext("QuestActObjTalkNpcGroup", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjTalkNpcGroup")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, взаимодействие с Npc...");

                var template = act.GetTemplate<QuestActObjTalkNpcGroup>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Npc, может быть не тот, что надо по квесту
                if (template.NpcGroupId != args.NpcGroupId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;

                // запускаем AI для Npc
                switch (component.NpcAiId)
                {
                    case QuestNpcAiName.FollowPath:
                        {
                            var route = component.AiPathName;
                            var npcs = WorldManager.Instance.GetAllNpcs();
                            foreach (var npc in npcs)
                            {
                                if (npc.TemplateId != template.NpcGroupId) { continue; }

                                if (npc.IsInPatrol) { break; }

                                switch (component.AiPathTypeId)
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
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Отписываемся от события.");
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, Event: 'OnTalkNpcGroupMade', Handler: 'OnTalkNpcGroupMadeHandler'");
            Owner.Events.OnTalkNpcGroupMade -= Owner.Quests.OnTalkNpcGroupMadeHandler; // отписываемся
            //Owner.Events.OnTalkNpcGroupMade += Owner.Quests.OnTalkNpcGroupMadeHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnReportNpcHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 330, 6198, 2531, 2532, 251
        var args = eventArgs as OnReportNpcArgs;
        if (args == null)
            return;

        //var step = Step; // сохраним, чтобы потом восстановить
        Step = QuestComponentKind.Ready;
        if (GetQuestContext("QuestActConReportNpc", out var context, out var listQuestActs))
            return;
        //Step = step;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActConReportNpc")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                var template =
                    act.GetTemplate<
                        QuestActConReportNpc>(); // для доступа к переменным требуется привидение к нужному типу

                Logger.Info($"[OnReportNpcHandler] Начинаем беседу с Npc {args.NpcId} о завершении квеста {TemplateId}.");

                // сначала проверим, что тот Npc, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    Logger.Info($"[OnReportNpcHandler] Это Npc {args.NpcId} не тот, что нужен нам {template?.NpcId}. Квест {TemplateId}.");
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnReportNpcHandler] Беседуем с Npc {args.NpcId} о завершении квеста {TemplateId}.");

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, опять проверяется тот ли Npc, что нужен
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
                Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, прверка акта {act.DetailType} дала результат {complete}.");
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
                Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, выполнен этап {componentIndex}, пробуем запустим скилл и/или баф.");
            }

            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnReportNpcHandler] Отписываемся от события.");
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing(args.Selected);

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnReportDoodadHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnReportDoodadArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActConReportDoodad", out var context, out var listQuestActs))
            return;

        var complete = false;
        var ThisIsNotWhatYouNeed = new List<bool>();
        for (var i = 0; i < context.State.CurrentComponents.Count; i++)
        {
            ThisIsNotWhatYouNeed.Add(false);
        }


        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActConReportDoodad")
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }

                Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, взаимодействие с doodad...");

                var template = act.GetTemplate<QuestActConReportDoodad>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Doodad, может быть не тот, что надо по квесту
                if (template?.DoodadId != args.DoodadId)
                {
                    ThisIsNotWhatYouNeed[componentIndex] = true;
                    continue;
                }
                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(Owner, this, 0); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                context.State.ContextResults[componentIndex] = complete;
            }
            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == true))
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        {
            Logger.Info($"[OnReportDoodadHandler] Отписываемся от события.");
            Logger.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, Event: 'OnReportDoodad', Handler: 'OnReportDoodadHandler'");
            Owner.Events.OnReportDoodad -= Owner.Quests.OnReportDoodadHandler; // отписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }

    #endregion Ready step

    #endregion Events
}

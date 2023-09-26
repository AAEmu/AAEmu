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
    public QuestContext QuestNoneState { get; set; }
    public QuestContext QuestStartState { get; set; }
    public QuestContext QuestSupplyState { get; set; }
    public QuestContext QuestProgressState { get; set; }
    public QuestContext QuestReadyState { get; set; }
    public QuestContext QuestRewardState { get; set; }

    // TODO здесь новый код для квестов
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

        _log.Info($"Quest Start: шага 'None' или'Start' нет в квесте {Id}");
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
    public void ContextProcessing()
    {
        _log.Info($"[ContextProcessing] квест {TemplateId}.");
        var next = true;
        while (next)
        {
            _log.Info($"[ContextProcessing][while] квест {TemplateId}.");
            switch (Step)
            {
                case QuestComponentKind.Supply when QuestSupplyState?.State?.CurrentQuestComponent != null:
                    switch (Status)
                    {
                        case QuestStatus.Progress when Condition == QuestConditionObj.Progress:
                        case QuestStatus.Ready when Condition == QuestConditionObj.Progress:
                            _log.Info($"[ContextProcessing][QuestSupplyState][Update] квест {TemplateId}.");
                            QuestSupplyState.State.Update();
                            break;
                        case QuestStatus.Ready when Condition == QuestConditionObj.Ready:
                            _log.Info($"[ContextProcessing][QuestSupplyState][Complete] квест {TemplateId}.");
                            QuestSupplyState.State.Complete();
                            Step++; // переход к следующему шагу
                            break;
                    }
                    break;
                case QuestComponentKind.Progress when QuestProgressState?.State?.CurrentQuestComponent != null:
                    switch (Status)
                    {
                        case QuestStatus.Progress when Condition == QuestConditionObj.Progress:
                        case QuestStatus.Ready when Condition == QuestConditionObj.Progress:
                            _log.Info($"[ContextProcessing][QuestProgressState][Update] квест {TemplateId}.");
                            if (!QuestProgressState.State.Update()) { next = false; } // подписка на события и прерываем цикл
                            break;
                        case QuestStatus.Ready when Condition == QuestConditionObj.Ready:
                            _log.Info($"[ContextProcessing][QuestProgressState][Complete] квест {TemplateId}.");
                            QuestProgressState.State.Complete();
                            Step++; // переход к следующему шагу
                            break;
                    }
                    break;
                case QuestComponentKind.Ready when QuestReadyState?.State?.CurrentQuestComponent != null:
                    switch (Status)
                    {
                        case QuestStatus.Progress when Condition == QuestConditionObj.Progress:
                        case QuestStatus.Ready when Condition == QuestConditionObj.Progress:
                            _log.Info($"[ContextProcessing][QuestReadyState][Update] квест {TemplateId}.");
                            if (!QuestReadyState.State.Update()) { next = false; } // подписка на события и прерываем цикл
                            break;
                        case QuestStatus.Ready when Condition == QuestConditionObj.Ready:
                            _log.Info($"[ContextProcessing][QuestReadyState][Complete] квест {TemplateId}.");
                            QuestReadyState.State.Complete();
                            Step++; // переход к следующему шагу
                            break;
                    }
                    break;
                case QuestComponentKind.Reward when QuestRewardState?.State?.CurrentQuestComponent != null:
                    switch (Status)
                    {
                        case QuestStatus.Progress when Condition == QuestConditionObj.Progress:
                        case QuestStatus.Ready when Condition == QuestConditionObj.Progress:
                            _log.Info($"[ContextProcessing][QuestRewardState][Update] квест {TemplateId}.");
                            QuestRewardState.State.Update();
                            break;
                        case QuestStatus.Ready when Condition == QuestConditionObj.Ready:
                            _log.Info($"[ContextProcessing][QuestRewardState][Complete] квест {TemplateId}.");
                            QuestRewardState.State.Complete();
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

    // Внимание!!!
    // для этих событий не будет известен QuestId и будет перебор всех активных квестов
    // что-бы по два раза не вызывались надо перед подпиской на событие отписываться!!!
    public void OnInteractionHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 3353
        // OnInteraction - похож на OnTalkMadeHandler
        var args = eventArgs as OnInteractionArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjInteraction", out var context, out var acts))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnInteractionHandler] Quest: {TemplateId}, взаимодействие с Doodad...");

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var actss = _questManager.GetActs(component.Id);
            foreach (var act in actss)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjInteraction")
                {
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjInteraction>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Npc, может быть не тот, что надо по квесту
                if (template?.DoodadId != args.DoodadId)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                //// проверка результатов на валидность (Validation of results)
                //if (Template.Selective)
                //{
                //    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                //    if (results.Any(b => b == true))
                //        selective = true;
                //}
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnInteractionHandler] Отписываемся от события.");
            _log.Info($"[OnInteractionHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnInteractionHandler] Quest: {TemplateId}, Event: 'OnInteraction', Handler: 'OnInteractionHandler'");
            Owner.Events.OnInteraction -= Owner.Quests.OnInteractionHandler; // отписываемся
            Owner.Events.OnInteraction += Owner.Quests.OnInteractionHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 250
        var args = eventArgs as OnMonsterHuntArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjMonsterHunt", out var context, out var listQuestActs))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Kill event triggered");

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjMonsterHunt")
                {
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template =
                    act.GetTemplate<QuestActObjMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                // проверка результатов на валидность (Validation of results)
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                    if (results.Any(b => b == true)) { selective = true; }
                }

                // если objective для текущего компонента готово, то запустим скилл и/или баф
                if (results.Count > 0 && results[componentIndex])
                {
                    UseSkillAndBuff(component);
                }
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnMonsterHuntHandler] Отписываемся от события.");
            _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterHunt', Handler: 'OnMonsterHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterHuntHandler; // отписываемся
            Owner.Events.OnMonsterHunt += Owner.Quests.OnMonsterHuntHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        ComponentId = 0;
        _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 250
        var args = eventArgs as OnMonsterGroupHuntArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjMonsterGroupHunt", out var context, out var listQuestActs))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Kill event triggered");

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjMonsterGroupHunt")
                {
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template =
                    act.GetTemplate<QuestActObjMonsterGroupHunt>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                if (!_questManager.CheckGroupNpc(template.QuestMonsterGroupId, args.NpcId))
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                // проверка результатов на валидность (Validation of results)
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                    if (results.Any(b => b == true)) { selective = true; }
                }

                // если objective для текущего компонента готово, то запустим скилл и/или баф
                if (results.Count > 0 && results[componentIndex])
                {
                    UseSkillAndBuff(component);
                }
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnMonsterGroupHuntHandler] Отписываемся от события.");
            _log.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterGroupHunt', Handler: 'OnMonsterGroupHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterGroupHuntHandler; // отписываемся
            Owner.Events.OnMonsterHunt += Owner.Quests.OnMonsterGroupHuntHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        ComponentId = 0;
        _log.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemUseHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 252
        var args = eventArgs as OnItemUseArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjItemUse", out var context, out var listQuestActs))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, использовали предмет из инвентаря...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjItemUse>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там использовали, может быть не то, что надо по квесту
                if (template?.ItemId != args.ItemId)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                // проверка результатов на валидность (Validation of results)
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                    if (results.Any(b => b == true)) { selective = true; }
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }

            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnItemUseHandler] Отписываемся от события.");
            _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Event: 'OnItemUse', Handler: 'OnItemUseHandler'");
            Owner.Events.OnItemUse -= Owner.Quests.OnItemUseHandler; // отписываемся
            Owner.Events.OnItemUse += Owner.Quests.OnItemUseHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 251, 324
        var args = eventArgs as OnItemGatherArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjItemGather", out var context, out var listQuestActs))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, в инвентарь добавился предмет...");

        var componentIndex = 0;
        foreach (var component in context.State.CurrentComponents)
        {
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                // проверка, что есть такой эвент для этого квеста
                if (act.DetailType != "QuestActObjItemGather")
                {
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjItemGather>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                if (template?.ItemId != args.ItemId)
                {
                    results.Add(false);
                    continue;
                }

                // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                var objectiveCount = Owner.Inventory.GetItemsCount(template.ItemId);

                // увеличиваем objective
                Objectives[componentIndex] = objectiveCount;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                // проверка результатов на валидность (Validation of results)
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                    if (results.Any(b => b == true)) { selective = true; }
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnItemGatherHandler] Отписываемся от события.");
            _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Event: 'OnItemGather', Handler: 'OnItemGatherHandler'");
            Owner.Events.OnItemGather -= Owner.Quests.OnItemGatherHandler; // отписываемся
            Owner.Events.OnItemGather += Owner.Quests.OnItemGatherHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemGroupGatherHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 251, 324
        var args = eventArgs as OnItemGroupGatherArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjItemGroupGather", out var context, out var listQuestActs))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, в инвентарь добавился предмет...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjItemGroupGather>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                if (!_questManager.CheckGroupItem(template.ItemGroupId, args.ItemId))
                {
                    results.Add(false);
                    continue;
                }

                // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                var objectiveCount = Owner.Inventory.GetItemsCount(template.ItemGroupId);

                // увеличиваем objective
                Objectives[componentIndex] = objectiveCount;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                // проверка результатов на валидность (Validation of results)
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                    if (results.Any(b => b == true)) { selective = true; }
                }
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnItemGroupGatherHandler] Отписываемся от события.");
            _log.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Event: 'OnItemGroupGather', Handler: 'OnItemGroupGatherHandler'");
            Owner.Events.OnItemGroupGather -= Owner.Quests.OnItemGroupGatherHandler; // отписываемся
            Owner.Events.OnItemGroupGather += Owner.Quests.OnItemGroupGatherHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnAggroArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjAggro", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnAggroHandler] Quest: {TemplateId}, кто-то аггрится...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjAggro>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (MathUtil.CalculateDistance(Owner.Transform.World.Position, args.Transform.World.Position) > template.Range)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnAggroHandler] Отписываемся от события.");
            _log.Info($"[OnAggroHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnAggroHandler] Quest: {TemplateId}, Event: 'OnAggro', Handler: 'OnAggroHandler'");
            Owner.Events.OnAggro -= Owner.Quests.OnAggroHandler; // отписываемся
            Owner.Events.OnAggro += Owner.Quests.OnAggroHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnAggroHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        // Quest: .
        var args = eventArgs as OnExpressFireArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjExpressFire", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Express Fire...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjExpressFire>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (template.ExpressKeyId != args.EmotionId)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnExpressFireHandler] Отписываемся от события.");
            _log.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Event: 'OnExpressFire', Handler: 'OnExpressFireHandler'");
            Owner.Events.OnExpressFire -= Owner.Quests.OnExpressFireHandler; // отписываемся
            Owner.Events.OnExpressFire += Owner.Quests.OnExpressFireHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 5967
        var args = eventArgs as OnAbilityLevelUpArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjAbilityLevel", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Ability Level Up...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                //var template = act.GetTemplate<QuestActObjAbilityLevel>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                //if (template.Level < Owner.Level)
                //{
                //    results.Add(false);
                //    continue;
                //}

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnAbilityLevelUpHandler] Отписываемся от события.");
            _log.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Event: 'OnExpressFire', Handler: 'OnAbilityLevelUpHandler'");
            Owner.Events.OnAbilityLevelUp -= Owner.Quests.OnAbilityLevelUpHandler; // отписываемся
            Owner.Events.OnAbilityLevelUp += Owner.Quests.OnAbilityLevelUpHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnLevelUpArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActObjLevel", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Level Up...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjLevel>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (template.Level >= Owner.Level)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnLevelUpHandler] Отписываемся от события.");
            _log.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Event: 'OnLevelUp', Handler: 'OnLevelUpHandler'");
            Owner.Events.OnLevelUp -= Owner.Quests.OnLevelUpHandler; // отписываемся
            Owner.Events.OnLevelUp += Owner.Quests.OnLevelUpHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 6024
        var args = eventArgs as OnCraftArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActObjCraft", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnCraftHandler] Quest: {TemplateId}, Level Up...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjCraft>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (template.CraftId != args.CraftId/* && Objectives[componentIndex] > template.Count*/)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnCraftHandler] Отписываемся от события.");
            _log.Info($"[OnCraftHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnCraftHandler] Quest: {TemplateId}, Event: 'OnCraft', Handler: 'OnCraftHandler'");
            Owner.Events.OnCraft -= Owner.Quests.OnCraftHandler; // отписываемся
            Owner.Events.OnCraft += Owner.Quests.OnCraftHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnCraftHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnCraftHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 6024
        var args = eventArgs as OnEnterSphereArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActObjSphere", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Level Up...");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjSphere>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, может быть не то, что надо по квесту
                if (component.Id != args.SphereQuest.ComponentID)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnEnterSphereHandler] Отписываемся от события.");
            _log.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
            Owner.Events.OnEnterSphere -= Owner.Quests.OnEnterSphereHandler; // отписываемся
            Owner.Events.OnEnterSphere += Owner.Quests.OnEnterSphereHandler; // снова пдписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }

    // Внимание!!!
    // для этого события будет известен QuestId
    public void OnTalkMadeHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2037
        // OnInteraction - похож на OnTalkMadeHandler
        var args = eventArgs as OnTalkMadeArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActObjTalk", out var context, out var listQuestActs))
            return;

        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, взаимодействие с Npc...");

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
                    //results.Add(false);
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActObjTalk>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Npc, может быть не тот, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

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
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnTalkMadeHandler] Отписываемся от события.");
            _log.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, Event: 'OnTalkMade', Handler: 'OnTalkMadeHandler'");
            Owner.Events.OnTalkMade -= Owner.Quests.OnTalkMadeHandler; // отписываемся
            Owner.Events.OnTalkMade += Owner.Quests.OnTalkMadeHandler; // снова подписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnReportNpcHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 330, 6198, 2531, 2532, 251
        var args = eventArgs as OnReportNpcArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActConReportNpc", out var context, out var listQuestActs))
            return;

        //var selective = false;
        var complete = false;
        var results = new List<bool>();
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnReportNpcHandler] Беседуем с Npc о завершении квеста {TemplateId}.");

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
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }

                var template = act.GetTemplate<QuestActConReportNpc>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что тот Npc, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    results.Add(false);
                    continue;
                }

                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(); // objective++

                // возвращается результат проверки, опять проверяется тот ли Npc, что нужен
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnReportNpcHandler] Отписываемся от события.");
            _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnReportDoodadHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnReportDoodadArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActConReportDoodad", out var context, out var listQuestActs))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;
        var ThisIsNotWhatYouNeed = false;

        _log.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, взаимодействие с doodad...");

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
                    //results.Add(false);
                    ThisIsNotWhatYouNeed = true;
                    continue;
                }
                var template = act.GetTemplate<QuestActConReportDoodad>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Doodad, может быть не тот, что надо по квесту
                if (template?.DoodadId != args.DoodadId)
                {
                    results.Add(false);
                    continue;
                }
                // увеличиваем objective
                Objectives[componentIndex]++;
                //template.Update(Owner, this, 0); // objective++

                // возвращается результат проверки, все ли предметы собрали или нет
                complete = act.Use(Owner, this, Objectives[componentIndex]);
                //complete = template.IsCompleted(Owner, this, 0);

                results.Add(complete);

                // проверка результатов на валидность (Validation of results)
                if (Template.Selective)
                {
                    // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                    if (results.Any(b => b == true))
                        selective = true;
                }
            }
            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (results.Count > 0 && results[componentIndex])
            {
                UseSkillAndBuff(component);
            }
            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed)
        {
            return;
        }

        // для завершения у всех objective компонентов должно быть выполнено
        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnReportDoodadHandler] Отписываемся от события.");
            _log.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, Event: 'OnReportDoodad', Handler: 'OnReportDoodadHandler'");
            Owner.Events.OnReportDoodad -= Owner.Quests.OnReportDoodadHandler; // отписываемся

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }

    // скорее всего не понадобятся
    public void OnQuestCompleteHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnQuestCompleteArgs;
        if (args == null)
            return;

        // компонент - выполнен, отписываемся
        _log.Info($"[OnQuestCompleteHandler] Отписываемся от события.");
        _log.Info($"[OnQuestCompleteHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
        _log.Info($"[OnQuestCompleteHandler] Quest: {TemplateId}, Event: 'OnQuestComplete', Handler: 'OnQuestCompleteHandler'");
        Owner.Events.OnQuestComplete -= Owner.Quests.OnQuestCompleteHandler;

        Status = QuestStatus.Ready;
        Condition = QuestConditionObj.Complete;
        Step++;// = QuestComponentKind.Reward;
        _log.Info($"[OnQuestCompleteHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.Quests.Complete(args.QuestId, args.Selected);
    }

    // на шаге Start - не нужны
    public void OnAcceptDoodadHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnAcceptDoodadArgs;
        if (args == null)
            return;

        if (GetQuestContext("QuestActConAcceptDoodad", out var context, out var acts))
            return;

        // проверка, что есть такой эвент для этого квеста
        if (acts.Any(act => act.DetailType != "QuestActConAcceptDoodad"))
            return;

        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;

        _log.Info($"[OnAcceptDoodadHandler] Quest: {TemplateId}, Взаимодействие с Doodad");

        var component = context.State.CurrentQuestComponent.GetFirstComponent();
        var componentIndex = context.State.CurrentQuestComponent.GetComponentCount();
        if (componentIndex > 1)
            throw new NotImplementedException();

        foreach (var act in acts)
        {
            // проверка, что есть такой эвент для этого квеста
            if (act.DetailType != "QuestActConAcceptDoodad")
                return;

            var template = act.GetTemplate<QuestActConAcceptDoodad>(); // для доступа к переменным требуется привидение к нужному типу

            // сначала проверим, что этотот Doodad, может быть не тот, что надо по квесту
            if (template.DoodadId != args.DoodadId)
            {
                results.Add(false);
                continue;
            }
            // возвращается результат проверки, все ли предметы собрали или нет
            complete = act.Use(Owner, this, 0);
            //complete = template.IsCompleted(Owner, this, 0);

            results.Add(complete);
        }

        if (results.All(b => b == true))
        {
            _log.Info($"[OnAcceptDoodadHandler] Отписываемся от события.");
            _log.Info($"[OnAcceptDoodadHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnAcceptDoodadHandler] Quest: {TemplateId}, Event: 'OnAcceptDoodad', Handler: 'OnAcceptDoodadHandler'");
            Owner.Events.OnAcceptDoodad -= Owner.Quests.OnAcceptDoodadHandler; // отписываемся
            ComponentId = component.Id;
            UseSkillAndBuff(component);

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            //Step++;
            _log.Info($"[OnAcceptDoodadHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();
            return;
        }

        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }

    #endregion Events
}

using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

public partial class Quest : PacketMarshaler
{
    #region NewQuestCode

    //public QuestContext CurrentState { get; set; }
    public QuestContext QuestNoneState { get; set; }
    public QuestContext QuestStartState { get; set; }
    public QuestContext QuestSupplyState { get; set; }
    public QuestContext QuestProgressState { get; set; }
    public QuestContext QuestReadyState { get; set; }
    public QuestContext QuestRewardState { get; set; }

    #endregion
    //#region NewQuestCode

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
    public bool StartQuest()
    {
        // шаг None или Start
        if (QuestNoneState?.State?.CurrentQuestComponent != null)
        {
            if (Status == QuestStatus.Invalid)
            {
                if (!QuestNoneState.State.Start()) { return false; } // сбросим принятый квест
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
                if (!QuestStartState.State.Start()) { return false; } // сбросим принятый квест
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
                            QuestSupplyState.State.Update(); // подписка на события
                            break;
                        case QuestStatus.Ready when Condition == QuestConditionObj.Ready:
                            _log.Info($"[ContextProcessing][QuestSupplyState][Complete] квест {TemplateId}.");
                            QuestSupplyState.State.Complete();
                            break;
                    }
                    Step++; // переход к следующему шагу
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

    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 250
        var args = eventArgs as OnMonsterHuntArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActObjMonsterHunt", out var context, out var acts))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;

        _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Kill event triggered");

        var component = context.State.CurrentQuestComponent.GetFirstComponent();
        var componentIndex = context.State.CurrentQuestComponent.GetComponentCount();
        if (componentIndex > 1)
            throw new NotImplementedException();

        foreach (var act in acts)
        {
            // проверка, что есть такой эвент для этого квеста
            if (act.DetailType != "QuestActObjMonsterHunt")
                return;

            try
            {
                var template = act.GetTemplate<QuestActObjMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    //results.Add(false);
                    //continue;
                    return;
                }
            }
            catch (Exception a)
            {
                _log.Warn(a);
            }

            // увеличиваем objective
            //template.Update(Owner, this, 0); // objective++
            Objectives[componentIndex - 1]++;

            // возвращается результат проверки, все ли предметы собрали или нет
            complete = act.Use(Owner, this, Objectives[componentIndex - 1]);
            //complete = template.IsCompleted(Owner, this, 0);

            results.Add(complete);

            // проверка результатов на валидность (Validation of results)
            if (Template.Selective)
            {
                // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                if (results.Any(b => b == true)) { selective = true; }
            }
        }

        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnMonsterHuntHandler] Отписываемся от события.");
            _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterHunt', Handler: 'OnMonsterHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterHuntHandler; // отписываемся
            ComponentId = component.Id;
            UseSkillAndBuff(component);

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        ComponentId = 0;
        _log.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnItemUseHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 252
        var args = eventArgs as OnItemUseArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActObjItemUse", out var context, out var acts))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;

        _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, использовали предмет из инвентаря...");

        var component = context.State.CurrentQuestComponent.GetFirstComponent();
        var componentIndex = context.State.CurrentQuestComponent.GetComponentCount();
        if (componentIndex > 1)
            throw new NotImplementedException();

        //var acts = _questManager.GetActs(component.Id);
        //foreach (var act in acts)
        foreach (var act in acts)
        {
            // проверка, что есть такой эвент для этого квеста
            if (act.DetailType != "QuestActObjItemUse")
                return;

            try
            {
                var template = act.GetTemplate<QuestActObjItemUse>(); // для доступа к переменным требуется привидение к нужному типу

                // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                if (template?.ItemId != args.ItemId)
                {
                    //results.Add(false);
                    //continue;
                    return;
                }
            }
            catch (Exception a)
            {
                _log.Warn(a);
            }

            // нужно посмотреть в инвентарь, так как после Start() ещё не знаем, есть предмет в инвентаре или нет (we need to look in the inventory, because after Start() we don't know yet if the item is in the inventory or not)
            //var objectiveCount = Owner.Inventory.GetItemsCount(template.ItemId);
            //if (objectiveCount < args.Count)
            //{
            //    // увеличиваем objective и ждем дальше
            //    template.Update(Owner, this, 0); // objective++
            //    Objectives[componentIndex - 1] = objectiveCount;
            //}
            //else
            //{
            //    // увеличиваем objective
            //    //template.Update(Owner, this, 0); // objective++
            //    Objectives[componentIndex - 1]++;
            //}
            Objectives[componentIndex - 1]++;

            // возвращается результат проверки, все ли предметы собрали или нет
            complete = act.Use(Owner, this, Objectives[componentIndex - 1]);
            //complete = template.IsCompleted(Owner, this, 0);

            results.Add(complete);

            // проверка результатов на валидность (Validation of results)
            if (Template.Selective)
            {
                // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                if (results.Any(b => b == true)) { selective = true; }
            }
        }

        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnItemUseHandler] Отписываемся от события.");
            _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Event: 'OnItemUse', Handler: 'OnItemUseHandler'");
            Owner.Events.OnItemUse -= Owner.Quests.OnItemUseHandler; // отписываемся
            ComponentId = component.Id;
            UseSkillAndBuff(component);

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

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
            throw new NotImplementedException();

        if (GetQuestContext("QuestActObjItemGather", out var context, out var acts))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;

        _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, в инвентарь добавился предмет...");

        var component = context.State.CurrentQuestComponent.GetFirstComponent();
        var componentIndex = context.State.CurrentQuestComponent.GetComponentCount();
        if (componentIndex > 1)
            throw new NotImplementedException();

        //var acts = _questManager.GetActs(component.Id);
        //foreach (var act in acts)
        foreach (var act in acts)
        {
            // проверка, что есть такой эвент для этого квеста
            if (act.DetailType != "QuestActObjItemGather")
                return;

            var objectiveCount = 0;
            try
            {
                var template = act.GetTemplate<QuestActObjItemGather>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                if (template?.ItemId != args.ItemId)
                {
                    //results.Add(false);
                    //continue;
                    return;
                }
                // нужно посмотреть в инвентарь, так как после Start() ещё не знаем, есть предмет в инвентаре или нет (we need to look in the inventory, because after Start() we don't know yet if the item is in the inventory or not)
                objectiveCount = Owner.Inventory.GetItemsCount(template.ItemId);
            }
            catch (Exception a)
            {
                _log.Warn(a);
            }

            // увеличиваем objective
            //template.Update(Owner, this, 0); // objective++
            Objectives[componentIndex - 1] = objectiveCount;
            //Objectives[componentIndex - 1]++;
            // возвращается результат проверки, все ли предметы собрали или нет
            complete = act.Use(Owner, this, Objectives[componentIndex - 1]);
            //complete = template.IsCompleted(Owner, this, 0);

            results.Add(complete);

            // проверка результатов на валидность (Validation of results)
            if (Template.Selective)
            {
                // разрешается быть подходящим одному предмету из нескольких (it is allowed to be matched to one item out of several)
                if (results.Any(b => b == true)) { selective = true; }
            }
        }

        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnItemGatherHandler] Отписываемся от события.");
            _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Event: 'OnItemGather', Handler: 'OnItemGatherHandler'");
            Owner.Events.OnItemGather -= Owner.Quests.OnItemGatherHandler; // отписываемся
            Owner.Events.OnItemGather += Owner.Quests.OnItemGatherHandler; // отписываемся
            ComponentId = component.Id;
            UseSkillAndBuff(component);

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnReportNpcHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 330, 6198, 2531, 2532, 251
        var args = eventArgs as OnReportNpcArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActConReportNpc", out var context, out var acts))
            return;

        //var selective = false;
        //var complete = false;
        var results = new List<bool>();

        _log.Info($"[OnReportNpcHandler] Беседуем с Npc о завершении квеста {TemplateId}.");

        var component = context.State.CurrentQuestComponent.GetFirstComponent();

        foreach (var act in acts)
        {
            // проверка, что есть такой эвент для этого квеста
            if (act.DetailType != "QuestActConReportNpc")
                return;

            try
            {
                var template = act?.GetTemplate<QuestActConReportNpc>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что тот Npc, что надо по квесту
                if (template?.NpcId != args.NpcId)
                {
                    //results.Add(false);
                    //continue;
                    return;
                }
            }
            catch (Exception a)
            {
                _log.Warn(a);
            }
            // возвращается результат проверки, опять проверяется тот ли Npc, что нужен
            //complete = act.Use(Owner, this, 0);
            //complete = template.IsCompleted(Owner, this, 0);

            results.Add(true);
        }

        if (results.All(b => results.Count != 0 && b == true))
        {
            _log.Info($"[OnReportNpcHandler] Отписываемся от события.");
            _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся

            ComponentId = component.Id;
            UseSkillAndBuff(component);

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }
    public void OnReportDoodadHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnReportDoodadArgs;
        if (args == null)
            throw new NotImplementedException();

        if (GetQuestContext("QuestActConReportDoodad", out var context, out var acts))
            return;

        var selective = false;
        var complete = false;
        var results = new List<bool>();
        EarlyCompletion = false;
        ExtraCompletion = false;

        _log.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, взаимодействие с doodad...");

        var component = context.State.CurrentQuestComponent.GetFirstComponent();
        var componentIndex = context.State.CurrentQuestComponent.GetComponentCount();
        if (componentIndex > 1)
            throw new NotImplementedException();

        foreach (var act in acts)
        {
            // проверка, что есть такой эвент для этого квеста
            if (act.DetailType != "QuestActConReportDoodad")
                return;

            try
            {
                var template = act.GetTemplate<QuestActConReportDoodad>(); // для доступа к переменным требуется привидение к нужному типу
                // сначала проверим, что этотот Doodad, может быть не тот, что надо по квесту
                if (template?.DoodadId != args.DoodadId)
                {
                    //results.Add(false);
                    //continue;
                    return;
                }
                // увеличиваем objective
                //template.Update(Owner, this, 0); // objective++
            }
            catch (Exception a)
            {
                _log.Warn(a);
            }

            Objectives[componentIndex - 1]++;

            // возвращается результат проверки, все ли предметы собрали или нет
            complete = act.Use(Owner, this, Objectives[componentIndex - 1]);
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

        if (results.All(b => results.Count != 0 && b == true) || selective && !(EarlyCompletion || ExtraCompletion))
        {
            _log.Info($"[OnReportDoodadHandler] Отписываемся от события.");
            _log.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");
            _log.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, Event: 'OnReportDoodad', Handler: 'OnReportDoodadHandler'");
            Owner.Events.OnReportDoodad -= Owner.Quests.OnReportDoodadHandler; // отписываемся
            ComponentId = component.Id;
            UseSkillAndBuff(component);

            Status = QuestStatus.Ready;
            Condition = QuestConditionObj.Ready;
            _log.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

            Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
            ContextProcessing();

            return;
        }

        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        _log.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character {Owner.Name}, ComponentId {ComponentId}, Step {Step}, Status {Status}, Condition {Condition}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
    }

    public void OnQuestCompleteHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnQuestCompleteArgs;
        if (args == null)
            throw new NotImplementedException();

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
            throw new NotImplementedException();

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

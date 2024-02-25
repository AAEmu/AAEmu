using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
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

    // перенес сюда переменные из QuestAcObj... для подсчета статуса квеста
    public int GroupGatherStatus { get; set; } = 0;
    public int GatherStatus { get; set; } = 0;
    public int HuntStatus { get; set; } = 0;
    public int GroupHuntStatus { get; set; } = 0;
    public int InteractionStatus { get; set; } = 0;
    public int ItemGroupUseStatus { get; set; } = 0;
    public int ItemUseStatus { get; set; } = 0;
    public int ItemObtainStatus { get; set; } = 0;

    #region Framework

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

            if (QuestProgressState?.State?.CurrentQuestComponent != null)
            {
                for (var i = 0; i < QuestProgressState.State.CurrentComponents.Count; i++)
                {
                    ProgressStepResults.Add(false);
                }
            }

            return true; // принимаем квест
        }

        Logger.Info($"Quest Start: шага 'None' или'Start' нет в квесте {Id}");
        return false; // не принимаем квест
    }

    public void GoToNextStep(int selected = 0)
    {
        Step++;
        while (true)
        {
            Logger.Info($"TRY NEXT STEP: Quest {TemplateId} going to step {Step}");

            switch (Step)
            {
                case QuestComponentKind.Supply when QuestSupplyState?.State is { CurrentQuestComponent: not null }:
                    QuestSupplyState.State.Start(false, selected);
                    return;
                case QuestComponentKind.Progress when QuestProgressState?.State is { CurrentQuestComponent: not null }:
                    QuestProgressState.State.Start(false, selected);
                    return;
                case QuestComponentKind.Ready when QuestReadyState?.State is { CurrentQuestComponent: not null }:
                    QuestReadyState.State.Start(false, selected);
                    return;
                case QuestComponentKind.Reward when QuestRewardState?.State is { CurrentQuestComponent: not null }:
                    QuestRewardState.State.Complete(selected);
                    return;
                case QuestComponentKind.None:
                case QuestComponentKind.Start:
                case QuestComponentKind.Fail:
                case QuestComponentKind.Drop:
                default:
                    Step++;
                    break;
                case QuestComponentKind.Reward + 1:
                    return;
            }
        }
    }

    public void UpdateActiveStep()
    {
        while (true)
        {
            Logger.Info($"TRY STEP & UPDATE: Quest {TemplateId} going to step {Step}");

            switch (Step)
            {
                case QuestComponentKind.Supply when QuestSupplyState?.State is { CurrentQuestComponent: not null }:
                    QuestSupplyState.State.Update();
                    return;
                case QuestComponentKind.Progress when QuestProgressState?.State is { CurrentQuestComponent: not null }:
                    QuestProgressState.State.Update();
                    return;
                case QuestComponentKind.Ready when QuestReadyState?.State is { CurrentQuestComponent: not null }:
                    QuestReadyState.State.Update();
                    return;
                case QuestComponentKind.Reward when QuestRewardState?.State is { CurrentQuestComponent: not null }:
                    QuestRewardState.State.Update();
                    return;
                case QuestComponentKind.None:
                case QuestComponentKind.Start:
                case QuestComponentKind.Fail:
                case QuestComponentKind.Drop:
                default:
                    Step++;
                    break;
                case QuestComponentKind.Reward + 1:
                    return;
            }
        }
    }
    public void CompleteActiveStep(int selected = 0, EventArgs eventArgs = null)
    {
        while (true)
        {
            Logger.Info($"TRY STEP & COMPLETE: Quest {TemplateId} going to step {Step}");

            switch (Step)
            {
                case QuestComponentKind.Supply when QuestSupplyState?.State is { CurrentQuestComponent: not null }:
                    QuestSupplyState.State.Complete(selected, eventArgs);
                    return;
                case QuestComponentKind.Progress when QuestProgressState?.State is { CurrentQuestComponent: not null }:
                    QuestProgressState.State.Complete(selected, eventArgs);
                    return;
                case QuestComponentKind.Ready when QuestReadyState?.State is { CurrentQuestComponent: not null }:
                    QuestReadyState.State.Complete(selected, eventArgs);
                    return;
                case QuestComponentKind.Reward when QuestRewardState?.State is { CurrentQuestComponent: not null }:
                    QuestRewardState.State.Complete(selected, eventArgs);
                    return;
                case QuestComponentKind.None:
                case QuestComponentKind.Start:
                case QuestComponentKind.Fail:
                case QuestComponentKind.Drop:
                default:
                    Step++;
                    break;
                case QuestComponentKind.Reward + 1:
                    return;
            }
        }
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
    public void ContextProcessing(int selected = 0, EventArgs eventArgs = null)
    {
        Logger.Info($"[ContextProcessing] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
        var next = true;
        while (next)
        {
            Logger.Info($"[ContextProcessing][while] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            switch (Step)
            {
                case QuestComponentKind.Supply when QuestSupplyState?.State is { CurrentQuestComponent: not null }:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestSupplyState][Update] Quest: {TemplateId}.");
                            QuestSupplyState.State.Update();
                            Condition = QuestConditionObj.Ready;
                            Logger.Info($"[ContextProcessing][QuestSupplyState][Update] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestSupplyState][Complete] Quest: {TemplateId}.");
                            QuestSupplyState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            Step++; // переход к следующему шагу // go to next step
                            Logger.Info($"[ContextProcessing][QuestSupplyState][Complete] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                    }

                    break;
                case QuestComponentKind.Progress when QuestProgressState?.State is { CurrentQuestComponent: not null }:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestProgressState][Update] Quest: {TemplateId}.");
                            if (!QuestProgressState.State.Update())
                            {
                                next = false;
                            } // подписка на события и прерываем цикл

                            //Condition = QuestConditionObj.Ready;
                            Logger.Info($"[ContextProcessing][QuestProgressState][Update] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestProgressState][Complete] Quest: {TemplateId}.");
                            if (!QuestProgressState.State.Complete(selected, eventArgs))
                            {
                                next = false;
                                Status = QuestStatus.Progress;
                                Condition = QuestConditionObj.Ready;
                                // ждем выполнение всех комронентов шага...
                                // wait for all step components to complete...
                            }
                            else
                            {
                                Status = QuestStatus.Ready;
                                Condition = QuestConditionObj.Progress;
                                Step++; // переход к следующему шагу // go to next step
                            }

                            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                            Logger.Info($"[ContextProcessing][QuestProgressState][Complete] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                    }

                    break;
                case QuestComponentKind.Ready when QuestReadyState?.State is { CurrentQuestComponent: not null }:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestReadyState][Update] Quest: {TemplateId}.");
                            if (!QuestReadyState.State.Update())
                            {
                                next = false;
                                Condition = QuestConditionObj.Progress;
                                Logger.Info($"[ContextProcessing][QuestReadyState][Update] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                                break;
                            } // подписка на события и прерываем цикл

                            Condition = QuestConditionObj.Ready;
                            Logger.Info($"[ContextProcessing][QuestReadyState][Update] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestReadyState][Complete] Quest: {TemplateId}.");
                            QuestReadyState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            Step++; // переход к следующему шагу // go to next step
                            Logger.Info($"[ContextProcessing][QuestReadyState][Complete] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                    }

                    break;
                case QuestComponentKind.Reward when QuestRewardState?.State is { CurrentQuestComponent: not null }:
                    switch (Condition)
                    {
                        case QuestConditionObj.Progress:
                            Logger.Info($"[ContextProcessing][QuestRewardState][Update] Quest: {TemplateId}.");
                            QuestRewardState.State.Update();
                            Condition = QuestConditionObj.Ready;
                            Logger.Info($"[ContextProcessing][QuestRewardState][Update] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
                            break;
                        case QuestConditionObj.Ready:
                            Logger.Info($"[ContextProcessing][QuestRewardState][Complete] квест {TemplateId}.");
                            QuestRewardState.State.Complete(selected);
                            Condition = QuestConditionObj.Progress;
                            next = false; // прерываем цикл
                            Logger.Info($"[ContextProcessing][QuestRewardState][Complete] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
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
        if (Step >= QuestComponentKind.Reward)
        {
            Step = QuestComponentKind.Ready;
        }

        if (Step < QuestComponentKind.Progress)
        {
            Step = QuestComponentKind.Progress;
        }

        switch (Step)
        {
            case QuestComponentKind.Progress when QuestProgressState?.State?.CurrentQuestComponent != null:
                QuestProgressState.State.Start();
                //Step = QuestComponentKind.Progress; // обновим
                Status = QuestStatus.Progress;
                Condition = QuestConditionObj.Progress;
                Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
                for (var i = 0; i < QuestProgressState.State.CurrentComponents.Count; i++)
                {
                    ProgressStepResults.Add(false);
                }

                break;
            case QuestComponentKind.Ready when QuestReadyState?.State?.CurrentQuestComponent != null:
                QuestReadyState.State.Start();
                //Step = QuestComponentKind.Ready; // обновим
                Status = QuestStatus.Ready;
                Condition = QuestConditionObj.Ready;
                Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));

                break;
        }
    }

    private bool GetQuestContext(out QuestContext context)
    {
        var step = Step;
        context = new QuestContext(this, new QuestProgressState(), QuestComponentKind.Progress);
        Step = step;

        switch (Step)
        {
            case QuestComponentKind.Progress when QuestProgressState.State.CurrentQuestComponent != null:
                context = QuestProgressState; // шаг Progress
                break;
            case QuestComponentKind.Ready when QuestReadyState.State.CurrentQuestComponent != null:
                context = QuestReadyState; // шаг Ready
                break;
            case QuestComponentKind.Reward when QuestRewardState.State.CurrentQuestComponent != null:
                context = QuestRewardState; // шаг Reward
                break;
            default:
                context = null;
                break;
        }

        return context == null;
    }

    private void BadChoice(string str)
    {
        // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        // Not all objective components are ready yet, we are waiting for the task to be completed
        ComponentId = 0;
        Status = QuestStatus.Progress;
        Condition = QuestConditionObj.Progress;
        Logger.Info($"[{str}] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
        Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
        UpdateActiveStep();
    }

    private void AltChoice(string str)
    {
        if ((EarlyCompletion || ExtraCompletion) && !ReadyToReportNpc)
        {
            Logger.Info($"{str} Подписываемся на событие.");
            Logger.Info($"{str} Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            // так как события OnReport содержат в параметре questId - нужна только подписка
            // since OnReport events contain questId in the parameter - only a subscription is needed
            //Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Owner.Events.OnReportNpc += Owner.Quests.OnReportNpcHandler; // подписываемся, что-бы сдать квест
            ReadyToReportNpc = true;
        }

        // проверка результатов на валидность, 266, 1125, 1135 - GroupHunt & ItemGather
        ComponentId = 0;
        Status = OverCompletionPercent >= 100
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        Condition = QuestConditionObj.Progress;
        Logger.Info($"{str} Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
        Logger.Info($"GroupGatherStatus: {GroupGatherStatus}, GatherStatus={GatherStatus}, HuntStatus={HuntStatus}, GroupHuntStatus={GroupHuntStatus}, InteractionStatus={InteractionStatus}, ItemGroupUseStatus={ItemGroupUseStatus}, ItemUseStatus={ItemUseStatus}, ItemObtainStatus={ItemObtainStatus}");

        Owner.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));
        UpdateActiveStep();
    }


    /// <summary>
    /// CheckResults - проверка компонентов и актов
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="context"></param>
    /// <param name="successive"></param>
    /// <param name="selective"></param>
    /// <param name="count"></param>
    /// <param name="letItDone"></param>
    /// <param name="score"></param>
    /// <param name="eventArgs"></param>
    /// <returns> -1 - не тот акт, 0 - проверка отрицательна, 1 - проверка положительна</returns>
    private int CheckResults<T>(QuestContext context, bool successive, bool selective, int count, bool letItDone, int score, EventArgs eventArgs) where T : QuestActTemplate
    {
        if (eventArgs == null)
        {
            return -1;
        }

        var ThisIsNotWhatYouNeed = new List<int>();
        //for (var i = 0; i < count; i++)
        //{
        //    ThisIsNotWhatYouNeed.Add(0);
        //}

        var results = false;
        var componentIndex = 0;
        var actIndex = 0;

        foreach (var component in context.State.CurrentComponents)
        {
            var complete = false;
            ComponentId = component.Id;
            var acts = _questManager.GetActs(component.Id);
            foreach (var act in acts)
            {
                ThisIsNotWhatYouNeed.Add(0);
                // проверка, что есть такой акт для этого квеста
                if (act.DetailType != typeof(T).Name)
                {
                    ThisIsNotWhatYouNeed[actIndex] = -1; // это не тот акт что нужен
                    continue;
                }

                if (Step == QuestComponentKind.Progress && ProgressStepResults[componentIndex])
                {
                    results = true;
                    Logger.Info($"Quest: {TemplateId}, Step={Step}, checking the act {act.DetailType} gave the result {complete}.");
                    continue; // уже выполнен компонент
                }

                complete = CheckAct(component, act, componentIndex);

                if (complete)
                {
                    results = true;
                }
                actIndex++;
            }

            // если objective для текущего компонента готово, то запустим скилл и/или баф
            if (complete)
            {
                UseSkillAndBuff(component);
            }

            componentIndex++;
        }

        if (ThisIsNotWhatYouNeed.All(b => b == -1))
        {
            return -1;
        }

        return results ? 1 : 0;

        // Helper function to checks this Quest Component Act is valid for the given EventArgs
        bool CheckAct(QuestComponent component, IQuestAct act, int idx)
        {
            switch (act.DetailType)
            {
                case "QuestActObjInteraction":
                    {
                        if (eventArgs is not OnInteractionArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjInteraction>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, что этотот Npc, может быть не тот, что надо по квесту
                        if (template?.DoodadId != args.DoodadId) { return false; }
                        break;
                    }
                case "QuestActObjMonsterHunt":
                    {
                        if (eventArgs is not OnMonsterHuntArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                        if (template?.NpcId != args.NpcId) { return false; }
                        break;
                    }
                case "QuestActObjMonsterGroupHunt":
                    {
                        if (eventArgs is not OnMonsterGroupHuntArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjMonsterGroupHunt>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, что убили того Npc, может быть не тот, что надо по квесту
                        if (!_questManager.CheckGroupNpc(template.QuestMonsterGroupId, args.NpcId)) { return false; }
                        break;
                    }
                case "QuestActObjItemUse":
                    {
                        if (eventArgs is not OnItemUseArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjItemUse>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, что там использовали, может быть не то, что надо по квесту
                        if (template?.ItemId != args.ItemId) { return false; }
                        break;
                    }
                case "QuestActObjItemGroupUse":
                    {
                        if (eventArgs is not OnItemGroupUseArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjItemGroupUse>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, что там использовали, может быть не то, что надо по квесту
                        if (!_questManager.CheckGroupItem(template.ItemGroupId, args.ItemGroupId)) { return false; }
                        break;
                    }
                case "QuestActObjItemGather":
                    {
                        if (eventArgs is not OnItemGatherArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjItemGather>(); // accessing variables requires casting to the desired type
                        // First, let's check what we picked up there, maybe it's not what we need for the quest
                        if (template?.ItemId != args.ItemId)
                        {
                            Logger.Info($"[OnItemGatherHandler] Quest={TemplateId}. Это предмет {args.ItemId} не тот, что нужен нам {template?.ItemId}.");
                            return false;
                        }

                        var objective = Owner.Inventory.GetItemsCount(template.ItemId);
                        Objectives[idx] = objective > 0 ? objective - 1 : 0; // we will show one less item, since there will be an increment later

                        break;
                    }
                case "QuestActObjItemGroupGather":
                    {
                        if (eventArgs is not OnItemGroupGatherArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjItemGroupGather>(); // accessing variables requires casting to the desired type
                        // First, let's check what we picked up there, maybe it's not what we need for the quest
                        if (!_questManager.CheckGroupItem(template.ItemGroupId, args.ItemId))
                        {
                            Logger.Info($"[OnItemGatherHandler] Quest={TemplateId}. Это предмет {args.ItemId} не тот, что нужен нам {template.ItemGroupId}.");
                            return false;
                        }

                        var objective = Owner.Inventory.GetItemsCount(args.ItemId);
                        Objectives[idx] = objective > 0 ? objective - 1 : 0; // we will show one less item, since there will be an increment later

                        break;
                    }
                case "QuestActObjZoneKill":
                    {
                        if (eventArgs is not OnZoneKillArgs args)
                            return false;

                        var template = act.GetTemplate<QuestActObjZoneKill>(); // для доступа к переменным требуется привидение к нужному типу

                        // Check quest conditions
                        // Check if we're in the correct zone
                        if ((template.ZoneId > 0) && (template.ZoneId != args.ZoneGroupId))
                            return false;

                        // Check level-range of the player
                        if ((args.Killer.Level < template.LvlMin) || (args.Killer.Level > template.LvlMax))
                            return false;

                        // Check if this requires a player kill
                        if ((template.CountPlayerKill > 0) && (args.Victim is Character))
                        {
                            // If it has a specific faction defined, check it
                            // PC Faction not Allowed
                            if ((template.PcFactionId > 0) && (template.PcFactionExclusive == true))
                            {
                                if (template.PcFactionId == args.Victim.Faction.Id)
                                    return false;
                            }
                            // PC Faction required
                            else if ((template.PcFactionId > 0) && (template.PcFactionExclusive == false))
                            {
                                if (template.PcFactionId != args.Victim.Faction.Id)
                                    return false;
                            }
                        }

                        // Check if this is a NPC kill
                        if ((template.CountNpc > 0) && (args.Victim is Npc))
                        {
                            // Check NPC level-range if needed
                            if ((template.LvlMinNpc > 0) && (template.LvlMaxNpc > 0) && ((args.Victim.Level < template.LvlMinNpc) || (args.Victim.Level > template.LvlMaxNpc)))
                                return false;

                            // If it has a specific faction defined, check it
                            // NPC Faction not Allowed
                            if ((template.NpcFactionId > 0) && (template.NpcFactionExclusive))
                            {
                                if (template.NpcFactionId == args.Victim.Faction.Id)
                                    return false;
                            }
                            // NPC Faction required
                            else if ((template.NpcFactionId > 0) && (template.NpcFactionExclusive == false))
                            {
                                if (template.NpcFactionId != args.Victim.Faction.Id)
                                    return false;
                            }
                        }
                        break;
                    }
                case "QuestActObjZoneMonsterHunt":
                    {
                        if (eventArgs is not OnZoneMonsterHuntArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjZoneMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template.ZoneId != args.ZoneGroupId) { return false; }
                        break;
                    }
                case "QuestActObjSphere":
                    {
                        if (eventArgs is not OnEnterSphereArgs args) { return false; }
                        //var template = act.GetTemplate<QuestActObjSphere>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (component.Id != args.SphereQuest.ComponentId) { return false; }
                        break;
                    }
                case "QuestActObjCraft":
                    {
                        if (eventArgs is not OnCraftArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjCraft>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template.CraftId != args.CraftId) { return false; }
                        break;
                    }
                case "QuestActObjLevel":
                    {
                        if (eventArgs is not OnLevelUpArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjLevel>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template.Level >= Owner.Level) { return false; }
                        break;
                    }
                case "QuestActObjAbilityLevel":
                    {
                        //if (eventArgs is not OnAbilityLevelUpArgs args) { return false; }
                        //var template = act.GetTemplate<QuestActObjAbilityLevel>(); // для доступа к переменным требуется привидение к нужному типу
                        //// сначала проверим, может быть не то, что надо по квесту
                        //if (template.Level >= Owner.Level) { return false; }
                        break;
                    }
                case "QuestActObjExpressFire":
                    {
                        if (eventArgs is not OnExpressFireArgs args) { return false; }
                        var expressKeyId = _expressTextManager.GetExpressAnimId(args.EmotionId);
                        var template = act.GetTemplate<QuestActObjExpressFire>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template.ExpressKeyId != expressKeyId) { return false; }
                        break;
                    }
                case "QuestActObjAggro":
                    {
                        if (eventArgs is not OnAggroArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjAggro>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (MathUtil.CalculateDistance(Owner.Transform.World.Position, args.Transform.World.Position) > template.Range) { return false; }
                        break;
                    }
                case "QuestActObjTalk":
                    {
                        if (eventArgs is not OnTalkMadeArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjTalk>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template?.NpcId != args.NpcId) { return false; }
                        break;
                    }
                case "QuestActObjTalkNpcGroup":
                    {
                        if (eventArgs is not OnTalkNpcGroupMadeArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjTalkNpcGroup>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template.NpcGroupId != args.NpcGroupId) { return false; }
                        break;
                    }
                case "QuestActConReportNpc":
                    {
                        if (eventArgs is not OnReportNpcArgs args) { return false; }
                        var template = act.GetTemplate<QuestActConReportNpc>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template?.NpcId != args.NpcId) { return false; }
                        break;
                    }
                case "QuestActConReportDoodad":
                    {
                        if (eventArgs is not OnReportDoodadArgs args) { return false; }
                        var template = act.GetTemplate<QuestActConReportDoodad>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template?.DoodadId != args.DoodadId) { return false; }
                        break;
                    }
            }

            Objectives[idx]++; // increase objective
            return act.Use(Owner, this, Objectives[idx]); // return the result of the check
        }
    }

    #endregion Framework

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
        {
            Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnInteractionHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnInteractionHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjInteraction>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnInteractionHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnInteractionHandler] Отписываемся от события.");
            Logger.Info($"[OnInteractionHandler] Quest: {TemplateId}, Event: 'OnInteraction', Handler: 'OnInteractionHandler'");
            Owner.Events.OnInteraction -= Owner.Quests.OnInteractionHandler; // отписываемся
            Owner.Events.OnInteraction += Owner.Quests.OnInteractionHandler; // снова подписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnInteractionHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnInteractionHandler");
    }
    public void OnMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 250, 1573, 4289
        var args = eventArgs as OnMonsterHuntArgs;
        if (args == null)
        {
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnMonsterHuntHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnMonsterHuntHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjMonsterHunt>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnMonsterHuntHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnMonsterHuntHandler] Отписываемся от события.");
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterHunt', Handler: 'OnMonsterHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterHuntHandler; // отписываемся
            Owner.Events.OnMonsterHunt += Owner.Quests.OnMonsterHuntHandler; // снова подписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnMonsterHuntHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnMonsterHuntHandler");
    }
    public void OnMonsterGroupHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 266, 1233, 4295
        var args = eventArgs as OnMonsterGroupHuntArgs;
        if (args == null)
        {
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnMonsterGroupHuntHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnMonsterGroupHuntHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjMonsterGroupHunt>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnMonsterGroupHuntHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnMonsterGroupHuntHandler] Отписываемся от события.");
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Event: 'OnMonsterGroupHunt', Handler: 'OnMonsterGroupHuntHandler'");
            Owner.Events.OnMonsterHunt -= Owner.Quests.OnMonsterGroupHuntHandler; // отписываемся
            Owner.Events.OnMonsterHunt += Owner.Quests.OnMonsterGroupHuntHandler; // снова подписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnMonsterGroupHuntHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnMonsterGroupHuntHandler");
    }
    public void OnItemUseHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 252, 1222
        var args = eventArgs as OnItemUseArgs;
        if (args == null)
        {
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnItemUseHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnItemUseHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjItemUse>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnItemUseHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnItemUseHandler] Unsubscribe from the event.");
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Event: 'OnItemUse', Handler: 'OnItemUseHandler'");
            Owner.Events.OnItemUse -= Owner.Quests.OnItemUseHandler; // отписываемся
            Owner.Events.OnItemUse += Owner.Quests.OnItemUseHandler; // снова подписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemUseHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnItemUseHandler");
    }
    public void OnItemGroupUseHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnItemGroupUseArgs;
        if (args == null)
        {
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnItemGroupUseHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnItemGroupUseHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjItemGroupUse>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnItemGroupUseHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnItemGroupUseHandler] Отписываемся от события.");
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Event: 'OnItemGroupUse', Handler: 'OnItemGroupUseHandler'");
            Owner.Events.OnItemGroupUse -= Owner.Quests.OnItemGroupUseHandler; // отписываемся
            Owner.Events.OnItemGroupUse += Owner.Quests.OnItemGroupUseHandler; // снова подписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemGroupUseHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnItemGroupUseHandler");
    }
    public void OnItemGatherHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 251, 324, 953, 1215, 1216, 1233, 2300
        var args = eventArgs as OnItemGatherArgs;
        if (args == null)
        {
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnItemGatherHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnItemGatherHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjItemGather>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnItemGatherHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnItemGatherHandler] Отписываемся от события.");
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Event: 'OnItemGather', Handler: 'OnItemGatherHandler'");
            Owner.Events.OnItemGather -= Owner.Quests.OnItemGatherHandler; // отписываемся
            if (args.QuestId == 0)
            {
                Owner.Events.OnItemGather += Owner.Quests.OnItemGatherHandler; // снова подписываемся
            }
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemGatherHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnItemGatherHandler");
    }
    public void OnItemGroupGatherHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnItemGroupGatherArgs;
        if (args == null)
        {
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnItemGroupGatherHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnItemGroupGatherHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjItemGroupGather>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnItemGroupGatherHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено или selective == true
        if (results && !(EarlyCompletion || ExtraCompletion))
        {
            Logger.Info($"[OnItemGroupGatherHandler] Отписываемся от события.");
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Event: 'OnItemGroupGather', Handler: 'OnItemGroupGatherHandler'");
            Owner.Events.OnItemGroupGather -= Owner.Quests.OnItemGroupGatherHandler; // отписываемся
            Owner.Events.OnItemGroupGather += Owner.Quests.OnItemGroupGatherHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnItemGroupGatherHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        AltChoice("OnItemGroupGatherHandler");
    }
    public void OnAggroHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnAggroArgs;
        if (args == null)
        {
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnAggroHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnAggroHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjAggro>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnAggroHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnAggroHandler] Отписываемся от события.");
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, Event: 'OnAggro', Handler: 'OnAggroHandler'");
            Owner.Events.OnAggro -= Owner.Quests.OnAggroHandler; // отписываемся
            Owner.Events.OnAggro += Owner.Quests.OnAggroHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnAggroHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        BadChoice("OnAggroHandler");
    }
    public void OnExpressFireHandler(object sender, EventArgs eventArgs)
    {
        // Quest:
        var args = eventArgs as OnExpressFireArgs;
        if (args == null)
        {
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnExpressFireHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnExpressFireHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjExpressFire>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnExpressFireHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnExpressFireHandler] Отписываемся от события.");
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Event: 'OnExpressFire', Handler: 'OnExpressFireHandler'");
            Owner.Events.OnExpressFire -= Owner.Quests.OnExpressFireHandler; // отписываемся
            Owner.Events.OnExpressFire += Owner.Quests.OnExpressFireHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnExpressFireHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnExpressFireHandler");
    }
    public void OnAbilityLevelUpHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 5967
        var args = eventArgs as OnAbilityLevelUpArgs;
        if (args == null)
        {
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnAbilityLevelUpHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnAbilityLevelUpHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjAbilityLevel>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnAbilityLevelUpHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnAbilityLevelUpHandler] Отписываемся от события.");
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Event: 'OnExpressFire', Handler: 'OnAbilityLevelUpHandler'");
            Owner.Events.OnAbilityLevelUp -= Owner.Quests.OnAbilityLevelUpHandler; // отписываемся
            Owner.Events.OnAbilityLevelUp += Owner.Quests.OnAbilityLevelUpHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnAbilityLevelUpHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnAbilityLevelUpHandler");
    }
    public void OnLevelUpHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnLevelUpArgs;
        if (args == null)
        {
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnLevelUpHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnLevelUpHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjLevel>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnLevelUpHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnLevelUpHandler] Отписываемся от события.");
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Event: 'OnLevelUp', Handler: 'OnLevelUpHandler'");
            Owner.Events.OnLevelUp -= Owner.Quests.OnLevelUpHandler; // отписываемся
            Owner.Events.OnLevelUp += Owner.Quests.OnLevelUpHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnLevelUpHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnLevelUpHandler");
    }
    public void OnCraftHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 6024, 4666, 4667, 4668, 4669
        var args = eventArgs as OnCraftArgs;
        if (args == null)
        {
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnCraftHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnCraftHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjCraft>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnCraftHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnCraftHandler] Отписываемся от события.");
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Event: 'OnCraft', Handler: 'OnCraftHandler'");
            Owner.Events.OnCraft -= Owner.Quests.OnCraftHandler; // отписываемся
            Owner.Events.OnCraft += Owner.Quests.OnCraftHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnCraftHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnCraftHandler");
    }
    public void OnEnterSphereHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2762 AcceptNpc & ObjSphere
        var args = eventArgs as OnEnterSphereArgs;
        if (args == null)
        {
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnEnterSphereHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnEnterSphereHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjSphere>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnEnterSphereHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnEnterSphereHandler] Отписываемся от события.");
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
            Owner.Events.OnEnterSphere -= Owner.Quests.OnEnterSphereHandler; // отписываемся
            Owner.Events.OnEnterSphere += Owner.Quests.OnEnterSphereHandler; // снова подписываемся

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnEnterSphereHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnEnterSphereHandler");
    }
    public void OnZoneKillHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2819 AcceptSphere & ZoneKill
        // Quest: 2820, 2821, 2822 AcceptNpc & ZoneKill
        if (eventArgs is not OnZoneKillArgs args)
        {
            Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, event has no arguments!");
            //BadChoice("OnZoneKillHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, trying to interact on the {Step} step instead of the Progress step!");
            //BadChoice("OnZoneKillHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjZoneKill>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, this event is not for this quest, exit ...");
            //BadChoice("OnZoneKillHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnZoneKillHandler] Отписываемся от события.");
            Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, Event: 'OnZoneKill', Handler: 'OnZoneKillHandler'");
            Owner.Events.OnZoneKill -= Owner.Quests.OnZoneKillHandler; // unsubscribe
            Owner.Events.OnZoneKill += Owner.Quests.OnZoneKillHandler; // subscribe again

            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnZoneKillHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);
            return;
        }

        BadChoice("OnZoneKillHandler");
    }
    public void OnZoneMonsterHuntHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2819 AcceptSphere & ZoneKill
        // Quest: 2820, 2821, 2822 AcceptNpc & ZoneKill
        var args = eventArgs as OnZoneMonsterHuntArgs;
        if (args == null)
        {
            Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnZoneMonsterHuntHandler");
            return;
        }

        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, попытка взаимодействовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnZoneMonsterHuntHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjZoneMonsterHunt>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnZoneMonsterHuntHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnZoneMonsterHuntHandler] Отписываемся от события.");
            Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, Event: 'OnZoneMonsterHunt', Handler: 'OnZoneMonsterHuntHandler'");
            Owner.Events.OnZoneMonsterHunt -= Owner.Quests.OnZoneMonsterHuntHandler; // отписываемся
            Owner.Events.OnZoneMonsterHunt += Owner.Quests.OnZoneMonsterHuntHandler; // снова подписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnZoneMonsterHuntHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnZoneMonsterHuntHandler");
    }

    /// <summary>
    /// OnTalkMadeHandler - для этого события будет известен QuestId, выполняется на шаге Progress
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnTalkMadeHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 2037
        // OnInteraction - похож на OnTalkMadeHandler
        var args = eventArgs as OnTalkMadeArgs;
        if (args == null)
        {
            Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnTalkMadeHandler");
            return;
        }

        // проверим расстояние до объекта
        // check the distance to the object
        if (MathUtil.CalculateDistance(args.Transform.World.Position, Owner.Transform.World.Position) > 4.0f)
        {
            Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, расстояние слишком далеко до объекта={args.NpcId}, чтобы завершить квест");
            //BadChoice("OnTalkMadeHandler");
            return;
        }

        // должен быть установлен шаг Progress для этого события!
        Step = QuestComponentKind.Progress;
        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, попытка беседовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnTalkMadeHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjTalk>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnTalkMadeHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnTalkMadeHandler] Отписываемся от события.");
            Logger.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnTalkMadeHandler] Quest: {TemplateId}, Event: 'OnTalkMade', Handler: 'OnTalkMadeHandler'");
            Owner.Events.OnTalkMade -= Owner.Quests.OnTalkMadeHandler; // отписываемся
            Owner.Events.OnTalkMade += Owner.Quests.OnTalkMadeHandler; // отписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnTalkMadeHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnTalkMadeHandler");
    }
    /// <summary>
    /// OnTalkNpcGroupMadeHandler - для этого события будет известен QuestId, выполняется на шаге Progress
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnTalkNpcGroupMadeHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 
        var args = eventArgs as OnTalkNpcGroupMadeArgs;
        if (args == null)
        {
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnTalkNpcGroupMadeHandler");
            return;
        }

        // проверим расстояние до объекта
        // check the distance to the object
        if (MathUtil.CalculateDistance(args.Transform.World.Position, Owner.Transform.World.Position) > 4.0f)
        {
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, расстояние слишком далеко до объекта={args.NpcGroupId}, чтобы завершить квест");
            //BadChoice("OnTalkNpcGroupMadeHandler");
            return;
        }

        // должен быть установлен шаг Progress для этого события!
        Step = QuestComponentKind.Progress;
        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, попытка беседовать на шаге {Step} вместо шага Progress!");
            //BadChoice("OnTalkNpcGroupMadeHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActObjTalkNpcGroup>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Отписываемся от события.");
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest: {TemplateId}, Event: 'OnTalkNpcGroupMade', Handler: 'OnTalkNpcGroupMadeHandler'");
            Owner.Events.OnTalkNpcGroupMade -= Owner.Quests.OnTalkNpcGroupMadeHandler; // отписываемся
            Owner.Events.OnTalkNpcGroupMade += Owner.Quests.OnTalkNpcGroupMadeHandler; // отписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnTalkNpcGroupMadeHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(0, eventArgs);
            CompleteActiveStep(0, eventArgs);

            return;
        }

        BadChoice("OnTalkNpcGroupMadeHandler");
    }

    #endregion Progress step

    #region Ready step

    /// <summary>
    /// OnReportNpcHandler - для этого события будет известен QuestId, выполняется на шаге Ready
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnReportNpcHandler(object sender, EventArgs eventArgs)
    {
        // Quest: 330, 6198, 2531, 2532, 251
        var args = eventArgs as OnReportNpcArgs;
        if (args == null)
        {
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnReportNpcHandler");
            return;
        }

        // проверим расстояние до объекта
        // check the distance to the object
        if (MathUtil.CalculateDistance(args.Transform.World.Position, Owner.Transform.World.Position) > 4.0f)
        {
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, расстояние слишком далеко до объекта={args.NpcId}, чтобы завершить квест");
            //BadChoice("OnReportNpcHandler");
            return;
        }

        // должен быть установлен шаг Ready для этого события!
        Step = QuestComponentKind.Ready;
        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, попытка беседовать на шаге {Step} вместо шага Ready!");
            //BadChoice("OnReportNpcHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, event triggered");

        // TODO По идее, если клиент инициировал событие, то это должен быть нужный Npc и проверки не нужны, а надо сразу завершать квест

        var res = CheckResults<QuestActConReportNpc>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnReportNpcHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnReportNpcHandler] Отписываемся от события.");
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Owner.Events.OnReportNpc -= Owner.Quests.OnReportNpcHandler; // отписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(args.Selected, eventArgs);
            CompleteActiveStep(args.Selected, eventArgs);
            return;
        }

        BadChoice("OnReportNpcHandler");
    }
    /// <summary>
    /// OnReportDoodadHandler - для этого события будет известен QuestId, выполняется на шаге Ready
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public void OnReportDoodadHandler(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnReportDoodadArgs;
        if (args == null)
        {
            Logger.Info($"[OnReportNpcHandler] Quest: {TemplateId}, нет аргументов у события!");
            //BadChoice("OnReportDoodadHandler");
            return;
        }

        // проверим расстояние до объекта
        // check the distance to the object
        if (MathUtil.CalculateDistance(args.Transform.World.Position, Owner.Transform.World.Position) > 8.0f)
        {
            Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, расстояние слишком далеко до объекта={args.DoodadId}, чтобы завершить квест");
            //BadChoice("OnReportDoodadHandler");
            return;
        }

        // должен быть установлен шаг Ready для этого события!
        Step = QuestComponentKind.Ready;
        if (GetQuestContext(out var context))
        {
            Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, попытка беседовать на шаге {Step} вместо шага Ready!");
            //BadChoice("OnReportDoodadHandler");
            return;
        }

        EarlyCompletion = false;
        ExtraCompletion = false;

        Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, event triggered");

        var res = CheckResults<QuestActConReportDoodad>(context, Template.Successive, Template.Selective, context.State.CurrentComponents.Count, Template.LetItDone, Template.Score, eventArgs);
        if (res == -1)
        {
            Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, это событие не для этого квеста, выход...");
            //BadChoice("OnReportDoodadHandler");
            return;
        }
        var results = res == 1;

        // для завершения у всех objective компонентов должно быть выполнено
        //if (context.State.ContextResults.All(b => context.State.ContextResults.Count != 0 && b == true))
        if (results)
        {
            Logger.Info($"[OnReportDoodadHandler] Отписываемся от события.");
            Logger.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            Logger.Info($"[OnReportDoodadHandler] Quest: {TemplateId}, Event: 'OnReportDoodad', Handler: 'OnReportDoodadHandler'");
            Owner.Events.OnReportDoodad -= Owner.Quests.OnReportDoodadHandler; // отписываемся
            Condition = QuestConditionObj.Ready;
            Logger.Info($"[OnReportDoodadHandler] Quest {TemplateId}, Character={Owner.Name}, ComponentId={ComponentId}, Step={Step}, Status={Status}, Condition={Condition}");
            //ContextProcessing(args.Selected, eventArgs);
            CompleteActiveStep(args.Selected, eventArgs);

            return;
        }

        BadChoice("OnReportDoodadHandler");
    }

    #endregion Ready step

    #endregion Events
}

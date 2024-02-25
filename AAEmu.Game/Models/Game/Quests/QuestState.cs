using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Quests;

// класс, определяющий состояние
// class defining the state
public abstract class QuestState
{
    protected static Logger Logger = LogManager.GetCurrentClassLogger();

    public Quest Quest { get; set; }
    public CurrentQuestComponent CurrentQuestComponent { get; set; }
    public List<QuestComponent> CurrentComponents { get; set; } = new();
    public List<QuestAct> CurrentActs { get; set; } = new();
    public QuestComponent CurrentComponent { get; set; }
    public abstract bool Start(bool forcibly = false, int selected = 0);
    public abstract bool Update();
    public abstract bool Complete(int selected = 0, EventArgs eventArgs = null);
    public abstract void Fail();
    public abstract void Drop();

    public bool CheckCount(IQuestAct act)
    {
        // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
        // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
        var objectiveCount = 0;
        var result = false;
        switch (act.DetailType)
        {
            case "QuestActObjMonsterHunt":
                {
                    //var template = act.GetTemplate<QuestActObjMonsterHunt>(); // для доступа к переменным требуется привидение к нужному типу
                    //objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.NpcId);
                    result = act.Use(Quest.Owner, Quest, objectiveCount);
                    break;
                }
            case "QuestActObjItemGather":
                {
                    var template = act.GetTemplate<QuestActObjItemGather>(); // для доступа к переменным требуется привидение к нужному типу
                    objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                    result = act.Use(Quest.Owner, Quest, objectiveCount);
                    break;
                }
            case "QuestActObjItemUse":
                {
                    //var template = act.GetTemplate<QuestActObjItemUse>(); // для доступа к переменным требуется привидение к нужному типу
                    //objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                    result = act.Use(Quest.Owner, Quest, objectiveCount);
                    break;
                }
            case "QuestActObjTalk":
                {
                    //var template = act.GetTemplate<QuestActObjTalk>(); // для доступа к переменным требуется привидение к нужному типу
                    //objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                    result = act.Use(Quest.Owner, Quest, objectiveCount);
                    break;
                }
            case "QuestActObjCraft":
                {
                    //var template = act.GetTemplate<QuestActObjCraft>(); // для доступа к переменным требуется привидение к нужному типу
                    //objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.CraftId);
                    result = act.Use(Quest.Owner, Quest, objectiveCount);
                    break;
                }
        }

        //var objective = act.Template.GetCount();
        //var result = CurrentQuestComponent.Execute(Quest.Owner, Quest, objectiveCount).Any(b => b == true);
        return result;
    }

    public bool CheckResults(QuestState context, bool successive, bool selective, int currentComponentCount, bool letItDone, int score, EventArgs eventArgs)
    {
        if (eventArgs == null)
        {
            return false;
        }

        var results = false;
        var componentIndex = 0;

        foreach (var component in context.CurrentComponents)
        {
            var complete = false;
            Quest.ComponentId = component.Id;
            var acts = QuestManager.Instance.GetActs(component.Id);
            foreach (var act in acts)
            {
                switch (act.DetailType)
                {
                    case "QuestActSupplyItem" when Quest.Step == QuestComponentKind.Progress:
                        {
                            // if SupplyItem = 0, we get the item
                            complete = act.Use(Quest.Owner, Quest, 0);
                            Quest.ProgressStepResults[componentIndex] = complete;
                            continue;
                        }
                }

                if (Quest.ProgressStepResults[componentIndex])
                {
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, checking the act {act.DetailType} already completed for part {componentIndex}.");
                    continue; // уже выполнен компонент
                }

                complete = CheckAct(component, act, componentIndex);

                Quest.ProgressStepResults[componentIndex] = complete;

                Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, checking the act {act.DetailType} gave the result {complete}.");
                // check the results for validity
                if (successive)
                {
                    // пока не знаю для чего это
                    // don't know what it's for yet
                    results = true;
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, something was successful Successive={successive}.");
                }
                else if (selective && Quest.ProgressStepResults.Any(b => b))
                {
                    // разрешается быть подходящим одному предмету из нескольких
                    // it is allowed to be matched to one item out of several
                    results = true;
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, allows you to make a choice Selective={selective}.");
                }
                else if (complete && currentComponentCount == 1 && !letItDone)
                {
                    // состоит из одного компонента и он выполнен
                    results = true;
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, the only one stage completed with the result {results}.");
                }
                else if (complete && score == 0 && currentComponentCount > 1 && !letItDone)
                {
                    // Должны быть выполнены все компоненты
                    // All components must be executed
                    results = Quest.ProgressStepResults.All(b => b == true);
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, stage {componentIndex} with result {complete}, all components must be executed.");
                }
                else if (complete && score == 0 && componentIndex == currentComponentCount - 1 && currentComponentCount > 1 && !letItDone)
                {
                    // выполнен последний компонент из нескольких
                    // the last component of several components is executed
                    results = true;
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, last {componentIndex} stage completed with result {results}.");
                }
                else if (Quest.OverCompletionPercent >= score && score != 0 && !letItDone)
                {
                    // выполнен один компонент из нескольких
                    results = true;
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, OverCompletionPercent component {componentIndex} with result {results}.");
                }
                else if (complete)
                {
                    results = true;
                    Logger.Info($"Quest: {Quest.TemplateId}, Step={Quest.Step}, completed component {componentIndex} with result {results}.");
                }
            }

            componentIndex++;
        }
        return results;

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
                        if (!QuestManager.Instance.CheckGroupNpc(template.QuestMonsterGroupId, args.NpcId)) { return false; }
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
                        if (!QuestManager.Instance.CheckGroupItem(template.ItemGroupId, args.ItemGroupId)) { return false; }
                        break;
                    }
                case "QuestActObjItemGather":
                    {
                        var template = act.GetTemplate<QuestActObjItemGather>(); // для доступа к переменным требуется привидение к нужному типу
                        var objectiveCount = Quest.Owner.Inventory.GetItemsCount(template.ItemId);
                        return act.Use(Quest.Owner, Quest, objectiveCount); // return the result of the check
                    }
                case "QuestActObjItemGroupGather":
                    {
                        if (eventArgs is not OnItemGroupGatherArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjItemGroupGather>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, что там подобрали, может быть не то, что надо по квесту
                        if (!QuestManager.Instance.CheckGroupItem(template.ItemGroupId, args.ItemId))
                        {
                            Logger.Info($"[OnItemGatherHandler] Quest={Quest.TemplateId}. Это предмет {args.ItemId} не тот, что нужен нам {template.ItemGroupId}.");
                            return false;
                        }
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
                        if (template.Level >= Quest.Owner.Level) { return false; }
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
                        var expressKeyId = ExpressTextManager.Instance.GetExpressAnimId(args.EmotionId);
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
                        if (MathUtil.CalculateDistance(Quest.Owner.Transform.World.Position, args.Transform.World.Position) > template.Range) { return false; }
                        break;
                    }
                case "QuestActObjTalk":
                    {
                        if (eventArgs is not OnTalkMadeArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjTalk>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template?.NpcId != args.NpcId) { return false; }
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
                                                throw new NotSupportedException(nameof(component.AiPathTypeId));
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
                                throw new NotSupportedException(nameof(component.NpcAiId));
                        }
                        break;
                    }
                case "QuestActObjTalkNpcGroup":
                    {
                        if (eventArgs is not OnTalkNpcGroupMadeArgs args) { return false; }
                        var template = act.GetTemplate<QuestActObjTalkNpcGroup>(); // для доступа к переменным требуется привидение к нужному типу
                        // сначала проверим, может быть не то, что надо по квесту
                        if (template.NpcGroupId != args.NpcGroupId) { return false; }
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
                                                throw new NotSupportedException(nameof(component.AiPathTypeId));
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
                                throw new NotSupportedException(nameof(component.NpcAiId));
                        }
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

            return act.Use(Quest.Owner, Quest, Quest.Objectives[idx]); // return the result of the check
        }
    }

    public void UpdateContext(Quest quest, QuestState questState, QuestContext questContext, QuestComponentKind questComponentKind)
    {
        var exit = false;

        // необходимо проверить, какие шаги имеются
        // need to check what steps are available
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
                        quest.Step = step;
                        quest.QuestNoneState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestNoneState.State.CurrentComponents = UpdateComponents();
                        quest.QuestNoneState.State.CurrentActs = UpdateActs();
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Start:
                    {
                        quest.QuestStartState = questContext;
                        quest.QuestStartState.State = this;
                        quest.QuestStartState.State.Quest = quest;
                        quest.Step = step;
                        quest.QuestStartState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestStartState.State.CurrentComponents = UpdateComponents();
                        quest.QuestStartState.State.CurrentActs = UpdateActs();
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Supply:
                    {
                        quest.QuestSupplyState = questContext;
                        quest.QuestSupplyState.State = this;
                        quest.QuestSupplyState.State.Quest = quest;
                        quest.Step = step;
                        quest.QuestSupplyState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestSupplyState.State.CurrentComponents = UpdateComponents();
                        quest.QuestSupplyState.State.CurrentActs = UpdateActs();
                        exit = true;
                        break;
                    }
                case QuestComponentKind.Progress:
                    {
                        quest.QuestProgressState = questContext;
                        quest.QuestProgressState.State = this;
                        quest.QuestProgressState.State.Quest = quest;
                        quest.Step = step;
                        quest.QuestProgressState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestProgressState.State.CurrentComponents = UpdateComponents();
                        quest.QuestProgressState.State.CurrentActs = UpdateActs();
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
                        quest.Step = step;
                        quest.QuestReadyState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestReadyState.State.CurrentComponents = UpdateComponents();
                        quest.QuestReadyState.State.CurrentActs = UpdateActs();
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
                        quest.Step = step;
                        quest.QuestRewardState.State.CurrentQuestComponent = UpdateComponent(questComponents);
                        quest.QuestRewardState.State.CurrentComponents = UpdateComponents();
                        quest.QuestRewardState.State.CurrentActs = UpdateActs();
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
            // collect components for the quest step
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
// Concrete classes representing different states
public class QuestNoneState : QuestState
{
    public override bool Start(bool forcibly = false, int selected = 0)
    {
        Logger.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId} начался!");

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
                //Quest.Condition = QuestConditionObj.Progress;
            }
            else
            {
                Quest.Status = results.Any(b => b == true) ? QuestStatus.Ready : QuestStatus.Progress;
            }
            switch (Quest.Status)
            {
                case QuestStatus.Progress:
                case QuestStatus.Ready:
                    //Quest.Condition = QuestConditionObj.Progress;
                    //Quest.Step++; // Supply
                    break;
                default:
                    Quest.Step = QuestComponentKind.Fail;
                    //Quest.Condition = QuestConditionObj.Fail;
                    break;
            }

            Logger.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        }
        else
        {
            Logger.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId} start failed.");
            Logger.Info($"[QuestNoneState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            return false; // останавливаемся на этом шаге, сигнал на удаление квеста
        }
        Quest.UseSkillAndBuff(CurrentComponent);

        return true;
    }
    public override bool Update()
    {
        Logger.Info($"[QuestNoneState][Update] Quest: {Quest.TemplateId}. Ничего не делаем.");
        Logger.Info($"[QuestNoneState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override bool Complete(int selected = 0, EventArgs eventArgs = null)
    {
        Logger.Info($"[QuestNoneState][Complete] Quest: {Quest.TemplateId}. Шаг успешно завершен!");
        Logger.Info($"[QuestNoneState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
        return true;
    }
    public override void Fail()
    {
        Logger.Info($"[QuestNoneState][Fail] Quest: {Quest.TemplateId} не может завершиться неудачей, пока не начался!");
        Logger.Info($"[QuestNoneState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        Logger.Info($"[QuestNoneState][Drop] Квест {Quest.TemplateId} сброшен");
        Logger.Info($"[QuestNoneState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
}

public class QuestStartState : QuestState
{
    public override bool Start(bool forcibly = false, int selected = 0)
    {
        Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId} начался!");

        var results = new List<bool>();
        if (forcibly)
        {
            CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
            results.Add(true); // применяем квест насильно командой '/quest add <questId>', даже если нет рядом нужного Npc
        }
        else
        {
            results = CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
        }

        foreach (var component in CurrentComponents)
        {
            // TODO вызов не требуется, так как выше мы уже вызвали все акты! 
            // TODO no call is required as we have already called all the acts above!
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
            //                Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId} настройка и старт таймера ограничения времени на квест!");
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

                    // проверим, что есть на этом шаге акт QuestActObjSphere
                    var progressContexts = Quest.QuestProgressState.State.CurrentQuestComponent.GetComponents();
                    foreach (var progressContext in progressContexts)
                    {
                        var acts = QuestManager.Instance.GetActs(progressContext.Id);
                        foreach (var act in acts)
                        {
                            switch (act?.DetailType)
                            {
                                case "QuestActObjSphere":
                                    {
                                        // подготовим работу QuestSphere
                                        // prepare QuestSphere's work
                                        Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}. Подписываемся на события, которые требуются для работы сферы");
                                        Quest.CurrentComponentId = progressContext.Id;

                                        if (Quest.AddQuestSphereTriggers(progressContext))
                                        {
                                            Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
                                            break;
                                        }

                                        // если сфера по какой-то причине отсутствует, будем считать, что мы её посетили
                                        // if the sphere is missing for some reason, we will assume that we have visited it
                                        Quest.Owner.SendMessage($"[Quest] Quest {Quest.TemplateId}, Sphere not found - skipped..");
                                        Logger.Info($"[QuestStartState][Start] Quest {Quest.TemplateId}, Sphere not found - skipped..");
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
                        //Quest.Condition = QuestConditionObj.Progress;
                        break;
                    default:
                        Quest.Step = QuestComponentKind.Fail;
                        //Quest.Condition = QuestConditionObj.Fail;
                        break;
                }

                Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            }
            else
            {
                Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId} start failed.");
                Logger.Info($"[QuestStartState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
                return false; // останавливаемся на этом шаге, сигнал на удаление квеста
            }

            Quest.UseSkillAndBuff(component);
        }

        return true;
    }
    public override bool Update()
    {
        Logger.Info($"[QuestStartState][Update] Quest: {Quest.TemplateId}. Ничего не делаем.");
        Logger.Info($"[QuestStartState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override bool Complete(int selected = 0, EventArgs eventArgs = null)
    {
        //Quest.Step = QuestComponentKind.Start;
        Logger.Info($"[QuestStartState][Complete] Quest: {Quest.TemplateId}. Шаг успешно завершен!");
        Logger.Info($"[QuestStartState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");

        return true;
    }
    public override void Fail()
    {
        Logger.Info($"[QuestStartState][Fail] Quest: {Quest.TemplateId} не может завершиться неудачей, пока не начался!");
        Logger.Info($"[QuestStartState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
    public override void Drop()
    {
        Logger.Info($"[QuestStartState][Drop] Quest: {Quest.TemplateId} сброшен");
        Logger.Info($"[QuestStartState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
}
public class QuestSupplyState : QuestState
{
    public override bool Start(bool forcibly = false, int selected = 0)
    {
        Logger.Info($"[QuestSupplyState][Start] Quest: {Quest.TemplateId}. Получим нужные предметы для прохождения квеста.");
        // получим квестовые предметы
        // get quest items
        CurrentQuestComponent.Execute(Quest.Owner, Quest, 0);
        Logger.Info($"[QuestSupplyState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
        return true;
    }

    public override bool Update()
    {
        Logger.Info($"[QuestSupplyState][Update] Quest: {Quest.TemplateId} в процессе выполнения.");
        Logger.Info($"[QuestSupplyState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }

    public override bool Complete(int selected = 0, EventArgs eventArgs = null)
    {
        Logger.Info($"[QuestSupplyState][Complete] Quest: {Quest.TemplateId}. Шаг успешно завершен!");
        Logger.Info($"[QuestSupplyState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
        return true;
    }

    public override void Fail()
    {
        Logger.Info($"[QuestSupplyState][Fail] Quest: {Quest.TemplateId} провален");
        Logger.Info($"[QuestSupplyState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }

    public override void Drop()
    {
        Logger.Info($"[QuestSupplyState][Drop] Quest: {Quest.TemplateId} сброшен");
        Logger.Info($"[QuestSupplyState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
}
public class QuestProgressState : QuestState
{
    public override bool Start(bool forcibly = false, int selected = 0)
    {
        bool res;
        var results2 = new List<bool>();
        CurrentComponent = CurrentQuestComponent.GetFirstComponent();

        Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}. Subscribe to events that are required for active acts.");

        for (var componentIndex = 0 ; componentIndex < CurrentComponents.Count ; componentIndex++)
        {
            var component = CurrentComponents[componentIndex];
            var acts = QuestManager.Instance.GetActs(component.Id);

            foreach (var act in acts)
            {
                switch (act?.DetailType)
                {
                    case "QuestActObjMonsterHunt":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnMonsterHunt -= Quest.Owner.Quests.OnMonsterHuntHandler;
                            Quest.Owner.Events.OnMonsterHunt += Quest.Owner.Quests.OnMonsterHuntHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnMonsterHunt', Handler: 'OnMonsterHuntHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjMonsterGroupHunt":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnMonsterGroupHunt -= Quest.Owner.Quests.OnMonsterGroupHuntHandler;
                            Quest.Owner.Events.OnMonsterGroupHunt += Quest.Owner.Quests.OnMonsterGroupHuntHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnMonsterGroupHunt', Handler: 'OnMonsterGroupHuntHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemGather":
                        {
                            // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            res = CheckCount(act);
                            //result2 = act.Template.IsCompleted();
                            if (res)
                            {
                                Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, QuestActObjItemGather already has the required items.'");
                                Quest.Objectives[componentIndex] = (act.Template as QuestActObjItemGather)?.Count ?? 0;
                                results2.Add(true); // уже выполнили задание, выход
                                break;
                            }

                            // подписка одна на всех
                            Quest.Owner.Events.OnItemGather -= Quest.Owner.Quests.OnItemGatherHandler;
                            Quest.Owner.Events.OnItemGather += Quest.Owner.Quests.OnItemGatherHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnItemGather', Handler: 'OnItemGatherHandler'");
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
                                Logger.Info($"[QuestProgressState][Start][QuestActObjItemGroupGather] Quest: {Quest.TemplateId}. Подписываться на событие не надо, так как в инвентаре уже лежать нужные вещи.");
                                results2.Add(true); // уже выполнили задание, выход
                                break;
                            }

                            // подписка одна на всех
                            Quest.Owner.Events.OnItemGroupGather -= Quest.Owner.Quests.OnItemGroupGatherHandler;
                            Quest.Owner.Events.OnItemGroupGather += Quest.Owner.Quests.OnItemGroupGatherHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnItemGroupGather', Handler: 'OnItemGroupGatherHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemUse":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnItemUse -= Quest.Owner.Quests.OnItemUseHandler;
                            Quest.Owner.Events.OnItemUse += Quest.Owner.Quests.OnItemUseHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnItemUse', Handler: 'OnItemUseHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjItemGroupUse":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnItemGroupUse -= Quest.Owner.Quests.OnItemGroupUseHandler;
                            Quest.Owner.Events.OnItemGroupUse += Quest.Owner.Quests.OnItemGroupUseHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnItemGroupUse', Handler: 'OnItemGroupUseHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjInteraction":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnInteraction -= Quest.Owner.Quests.OnInteractionHandler;
                            Quest.Owner.Events.OnInteraction += Quest.Owner.Quests.OnInteractionHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnInteraction', Handler: 'OnInteractionHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjLevel":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnLevelUp -= Quest.Owner.Quests.OnLevelUpHandler;
                            Quest.Owner.Events.OnLevelUp += Quest.Owner.Quests.OnLevelUpHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnLevelUp', Handler: 'OnLevelUpHandler'");
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
                            Quest.Owner.Events.OnZoneKill -= Quest.Owner.Quests.OnZoneKillHandler;
                            Quest.Owner.Events.OnZoneKill += Quest.Owner.Quests.OnZoneKillHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnZoneKill', Handler: 'OnZoneKillHandler'");
                            results2.Add(false); // we'll wait for the event
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
                            Quest.CurrentComponentId = component.Id;
                            // подписка одна на всех

                            Quest.AddQuestSphereTriggers(component);

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnEnterSphere', Handler: 'OnEnterSphereHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjTalk":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnTalkMade -= Quest.Owner.Quests.OnTalkMadeHandler;
                            Quest.Owner.Events.OnTalkMade += Quest.Owner.Quests.OnTalkMadeHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnTalkMade', Handler: 'OnTalkMadeHandler'");
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

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnTalkNpcGroupMade', Handler: 'OnTalkNpcGroupMadeHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjExpressFire":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnExpressFire -= Quest.Owner.Quests.OnExpressFireHandler;
                            Quest.Owner.Events.OnExpressFire += Quest.Owner.Quests.OnExpressFireHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnExpressFire', Handler: 'OnExpressFireHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjAggro":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnAggro -= Quest.Owner.Quests.OnAggroHandler;
                            Quest.Owner.Events.OnAggro += Quest.Owner.Quests.OnAggroHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnAggro', Handler: 'OnAggroHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjAbilityLevel":
                        {
                            // подписка одна на всех
                            Quest.Owner.Events.OnAbilityLevelUp -= Quest.Owner.Quests.OnAbilityLevelUpHandler;
                            Quest.Owner.Events.OnAbilityLevelUp += Quest.Owner.Quests.OnAbilityLevelUpHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnAbilityLevelUp', Handler: 'OnAbilityLevelUpHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActObjCraft":
                        {
                            // нужно посмотреть в инвентарь, так как ещё не знаем, есть предмет в инвентаре или нет
                            // we need to look in the inventory, because we don't know yet if the item is in the inventory or not
                            //res = CheckCount(act);
                            ////result2 = act.Template.IsCompleted();
                            //if (res)
                            //{
                            //    results2.Add(true); // уже выполнили задание, выход
                            //    break;
                            //}

                            // подписка одна на всех
                            Quest.Owner.Events.OnCraft -= Quest.Owner.Quests.OnCraftHandler;
                            Quest.Owner.Events.OnCraft += Quest.Owner.Quests.OnCraftHandler;

                            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Event: 'OnCraft', Handler: 'OnCraftHandler'");
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

        // All parts are completed?
        if (results2.All(b => b == true))
        {
            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}. There is no need to subscribe to the event, since the necessary things are already in your inventory.");
            Quest.ComponentId = CurrentComponent.Id;
            Quest.Status = QuestStatus.Ready;
            //Quest.Step = QuestComponentKind.Progress;
            Quest.Condition = QuestConditionObj.Ready;
            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));
            Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
            return true;
        }

        // Some parts are completed?
        if (results2.Any(b => b == true))
        {
            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}. Some of the objectives have already completed, but not all of them.");
            Quest.ComponentId = 0;
            Quest.Status = QuestStatus.Progress;
            Quest.Step = QuestComponentKind.Progress;
            Quest.Condition = QuestConditionObj.Progress;
            Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));
            // Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
            return true;
        }

        // None of the objectives have been completed
        // подписка на события и прерываем цикл
        // Subscribed to events and break the loop
        Quest.ComponentId = 0;
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Progress;
        Logger.Info($"[QuestProgressState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return false;
    }

    public override bool Update()
    {
        Logger.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId} already in progress.");
        Logger.Info($"[QuestProgressState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }

    public override bool Complete(int selected = 0, EventArgs eventArgs = null)
    {
        var results = CheckResults(this, Quest.Template.Successive, Quest.Template.Selective, CurrentComponents.Count, Quest.Template.LetItDone, Quest.Template.Score, eventArgs);
        if (results)
        {
            Logger.Info($"[QuestProgressState][Complete] Quest: {Quest.TemplateId}. Step completed successfully!");
            Quest.Status = QuestStatus.Ready;
            Quest.Condition = QuestConditionObj.Progress;
            Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));
            Logger.Info($"[QuestProgressState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
            return true;
        }

        Logger.Info($"[QuestProgressState][Complete] Quest: {Quest.TemplateId}. Not all components are completed!");
        // ждем выполнение всех комронентов шага...
        // wait for all step components to complete...
        Quest.Status = QuestStatus.Progress;
        Quest.Condition = QuestConditionObj.Ready;
        Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));
        Logger.Info($"[QuestProgressState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return false;
    }

    public override void Fail()
    {
        Logger.Info($"[QuestProgressState][Fail] Quest: {Quest.TemplateId} провален");
        Logger.Info($"[QuestProgressState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }

    public override void Drop()
    {
        Logger.Info($"[QuestProgressState][Drop] Quest: {Quest.TemplateId} сброшен");
        Logger.Info($"[QuestProgressState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
}

public class QuestReadyState : QuestState
{
    public override bool Start(bool forcibly = false, int selected = 0)
    {
        CurrentComponent = CurrentQuestComponent.GetFirstComponent();

        Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}. Подписываемся на события, которые требуются для активных актов");

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
                            Quest.ReadyToReportNpc = true;
                            Quest.Owner.Events.OnReportNpc += Quest.Owner.Quests.OnReportNpcHandler;
                            Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Event: 'OnReportNpc', Handler: 'OnReportNpcHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActConReportDoodad":
                        {
                            Quest.Owner.Events.OnReportDoodad += Quest.Owner.Quests.OnReportDoodadHandler;
                            Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Event: 'OnReportDoodad', Handler: 'OnReportDoodadHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActConReportJournal":
                        {
                            //Quest.Owner.Events.OnReportJournal += Quest.Owner.Quests.OnReportJournalHandler;
                            Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Event: 'OnReportJournal', Handler: 'OnReportJournalHandler'");
                            results2.Add(false); // будем ждать события
                            break;
                        }
                    case "QuestActConAutoComplete":
                        {
                            //Quest.Owner.Events.OnQuestComplete += Quest.Owner.Quests.OnQuestCompleteHandler;
                            Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Event: 'OnQuestComplete', Handle: 'OnEventsOnQuestComplete'");
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
            //Quest.Step = QuestComponentKind.Ready;
            Quest.Condition = QuestConditionObj.Ready;
            Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
            Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));
            Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
            return true;
        }

        Quest.ComponentId = 0;
        //Quest.Step = QuestComponentKind.Ready;
        Logger.Info($"[QuestReadyState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        //Quest.Owner.SendPacket(new SCQuestContextUpdatedPacket(Quest, Quest.ComponentId));
        return false;
    }
    public override bool Update()
    {
        Logger.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId} уже в процессе выполнения.");
        Logger.Info($"[QuestReadyState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }
    public override bool Complete(int selected = 0, EventArgs eventArgs = null)
    {
        Logger.Info($"[QuestReadyState][Complete] Quest: {Quest.TemplateId}. Шаг успешно завершен!");
        Logger.Info($"[QuestReadyState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        Quest.GoToNextStep(selected); // переход к следующему шагу // go to next step
        return true;
    }
    public override void Fail()
    {
        Logger.Info($"[QuestReadyState][Fail] Квест {Quest.TemplateId} провален");
        Logger.Info($"[QuestReadyState][Fail] Квест {Quest.TemplateId} Подписываемся на события, которые требуются для активных актов");
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
        Logger.Info($"[QuestReadyState][Drop] Квест {Quest.TemplateId} сброшен");
        Logger.Info($"[QuestReadyState][Drop] Квест {Quest.TemplateId} уже завершен. Нельзя провалить.");
    }
}

public class QuestRewardState : QuestState
{
    public override bool Start(bool forcibly = false, int selected = 0)
    {
        Logger.Info($"[QuestRewardState][Start] Quest: {Quest.TemplateId}. Получаем бонусы.");

        // получение бонусов организовано в Quests.Complete
        //Quest.Step = QuestComponentKind.Reward;
        Logger.Info($"[QuestRewardState][Start] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }

    public override bool Update()
    {
        Logger.Info($"[QuestRewardState][Update] Quest: {Quest.TemplateId}. Уже в процессе выполнения.");
        Logger.Info($"[QuestRewardState][Update] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        return true;
    }

    public override bool Complete(int selected = 0, EventArgs eventArgs = null)
    {
        Logger.Info($"[QuestRewardState][Complete] Quest: {Quest.TemplateId}. Завершаем квест.");
        Logger.Info($"[QuestRewardState][Complete] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
        Quest.Owner.Quests.Complete(Quest.TemplateId, selected); // Завершаем квест
        return true;
    }

    public override void Fail()
    {
        Logger.Info($"[QuestRewardState][Fail] Quest: {Quest.TemplateId} уже завершен. Нельзя провалить.");
        Logger.Info($"[QuestRewardState][Fail] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }

    public override void Drop()
    {
        Logger.Info($"[QuestRewardState][Drop] Quest: {Quest.TemplateId} уже завершен. Нельзя сбросить.");
        Logger.Info($"[QuestRewardState][Drop] Quest: {Quest.TemplateId}, Character {Quest.Owner.Name}, ComponentId {Quest.ComponentId}, Step {Quest.Step}, Status {Quest.Status}, Condition {Quest.Condition}");
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

public partial class Quest
{
    private Dictionary<QuestComponentKind, QuestStep> QuestSteps { get; set; } = new();

    #region Framework

    /// <summary>
    /// Initialize all the steps needed for the quest by grabbing the templates and creating live variants of them
    /// </summary>
    public void CreateQuestSteps()
    {
        QuestSteps.Clear(); // Clear it just to be sure
        // Add Components to Steps (and create them)
        foreach (var (componentId, componentTemplate) in Template.Components)
        {
            if (!QuestSteps.TryGetValue(componentTemplate.KindId, out var step))
            {
                step = new QuestStep(componentTemplate.KindId, this);
                QuestSteps.Add(componentTemplate.KindId, step);
            }

            var newComponent = new QuestComponent(step, componentTemplate);
            step.Components.Add(componentId, newComponent);

            // Add Acts (if any)
            foreach (var questActTemplate in newComponent.Template.ActTemplates)
            {
                var newAct = new QuestAct(newComponent, questActTemplate);
                newComponent.Acts.Add(newAct);
            }
        }

        if (QuestSteps.Count <= 0)
            Logger.Warn($"Quest {TemplateId} does not seem to have any components!");
    }

    /// <summary>
    /// Starting the initial step of the quest, only call from CharacterQuests.AddQuest()
    /// </summary>
    /// <returns>False if this quest does not have a start section</returns>
    public bool StartQuest()
    {
        if (!QuestSteps.TryGetValue(QuestComponentKind.Start, out var stepStart))
        {
            Logger.Warn($"Tried to start a quest without a starter component Quest: {TemplateId}");
            return false;
        }

        Step = QuestComponentKind.Start;
        // Send the first components, or the one that's used to start this ?
        ComponentId = stepStart.Components.Values.FirstOrDefault()?.Template.Id ?? 0;
        Owner.SendPacket(new SCQuestContextStartedPacket(this, ComponentId));
        Logger.Debug($"StartQuest, Quest:{TemplateId}, Player {Owner.Name} ({Owner.Id})");
        return true;
    }

    /// <summary>
    /// Checks or Executes all components in the current step and goes to the next one if completed
    /// </summary>
    /// <returns></returns>
    public bool RunCurrentStep()
    {
        if (!Steps.TryGetValue(Step, out var questStep))
            return false;

        var res = questStep.RunComponents();

        if (res)
        {
            GoToNextStep();
        }

        /*
        ComponentId = 0;
        Status = stepResult >= QuestObjectiveStatus.QuestComplete
            ? QuestStatus.Ready // квест можно сдать, но мы не даем ему закончиться при достижении 100% пока сами не подойдем к Npc сдавать квест
            : QuestStatus.Progress; // пока еще не у всех компонентов objective готовы, ожидаем выполнения задания
        Condition = QuestConditionObj.Progress;

        */
        
        // Send update to player
        Owner?.SendPacket(new SCQuestContextUpdatedPacket(this, ComponentId));

        return res;
    }

    /// <summary>
    /// Move the Step to the next logical Step
    /// </summary>
    private void GoToNextStep()
    {
        // Loop through the flow of steps until we get a valid one
        var lastStep = Step;
        var loopLockCheck = 0;
        while (true)
        {
            // Shouldn't happen, but check for infinite loops
            if (loopLockCheck > 20)
            {
                Logger.Error($"GoToNextStep got stuck in a infinite loop for Quest:{TemplateId}, Step:{lastStep} -> {Step}, Player {Owner.Name} ({Owner.Id}");
                break;
            }
            loopLockCheck++;
            // Change action based on current step
            switch (Step)
            {
                case QuestComponentKind.None:
                    // This step is only used in legacy task boards, but we go through it anyway
                    Step = QuestComponentKind.Supply; // Next we assume Supply
                    break;
                case QuestComponentKind.Start:
                    Step = QuestComponentKind.None; // Actually go past None first before going to Supply
                    break;
                case QuestComponentKind.Supply:
                    Step = QuestComponentKind.Progress; // After giving the Supply Items, go to Progress
                    break;
                case QuestComponentKind.Progress:
                    Step = QuestComponentKind.Ready; // When Objectives completed, go to Ready
                    break;
                case QuestComponentKind.Fail:
                    // Fail state does not have a next step, player needs to manually drop/restart the quest
                    return;
                case QuestComponentKind.Ready:
                    Step = QuestComponentKind.Reward; // Go to Reward when turning in the quest 
                    break;
                case QuestComponentKind.Drop:
                    // This quest is being dropped, there is no next step
                    Owner.Quests.DropQuest(TemplateId, true, false);
                    return;
                case QuestComponentKind.Reward:
                    // Reward is the last possible step

                    // Mark quest as completed
                    var completedBlock = Owner.Quests.SetCompletedQuestFlag(TemplateId, true);
                    // copy body data for packet
                    var body = new byte[8];
                    completedBlock.Body.CopyTo(body, 0);

                    Owner.Quests.DropQuest(TemplateId, true, false);
                    Owner.SendPacket(new SCQuestContextCompletedPacket(TemplateId, body, 0));

                    return;
                default:
                    Logger.Warn($"Quest GoToNextStep failed for Step:{Step}, Quest:{TemplateId}, Player:{Owner.Name} ({Owner.Id}");
                    return;
            }

            // If the new current step is valid for this quest template, exit the loop
            if (QuestSteps.ContainsKey(Step))
                break;
        }
    }

    #endregion Framework

    /// <summary>
    /// Changes the current Step of the quest, and takes care of the event handlers
    /// </summary>
    /// <param name="value"></param>
    private void SetStep(QuestComponentKind value)
    {
        if (value == _step)
            return;

        // Finalize old Step (if any)
        if (QuestSteps.TryGetValue(_step, out var oldQuestSteps))
            oldQuestSteps.FinalizeStep();

        // Set new Value
        _step = value;

        // Reset active component (used by packet only) 
        ComponentId = 0;

        // Initialize Acts for this Step (if any)
        if (QuestSteps.TryGetValue(value, out var questSteps))
            questSteps.InitializeStep();

        // Trigger OnQuestStepChanged event, even if this step is not available
        Owner?.Events?.OnQuestStepChanged(Owner, new OnQuestStepChangedArgs() { QuestId = TemplateId, Step = value });
        RequestEvaluation();
    }

    /// <summary>
    /// Checks is this specific QuestAct is completed, checks objectives
    /// </summary>
    /// <returns></returns>
    public QuestObjectiveStatus GetQuestObjectiveStatus()
    {
        if (!QuestSteps.TryGetValue(Step, out var currentStep))
            return QuestObjectiveStatus.NotReady;

        var questComponents = currentStep.Components.Values ;
        if (Template.Score > 0)
        {
            // Use Score Handler
            var score = 0;
            // Loop through all components and acts to calculate their score
            foreach (var questComponent in questComponents)
            foreach (var questComponentAct in questComponent.Acts)
                score += questComponentAct.Template.Count * Objectives[questComponentAct.Template.ThisComponentObjectiveIndex];

            // Check the score results
            if (Template.LetItDone && score >= Template.Score * 3 / 2)
                return QuestObjectiveStatus.Overachieved;
            if (Template.LetItDone && score > Template.Score)
                return QuestObjectiveStatus.ExtraProgress;
            if (score >= Template.Score)
                return QuestObjectiveStatus.QuestComplete;
            if (Template.LetItDone && (score >= Template.Score * 1 / 2))
                return QuestObjectiveStatus.CanEarlyComplete;

            return QuestObjectiveStatus.NotReady;
        }

        // Check individual act counts if it's not score based
        foreach (var questComponent in questComponents)
        foreach (var questComponentAct in questComponent.Acts)
        {
            if (Template.LetItDone && Objectives[questComponentAct.Template.ThisComponentObjectiveIndex] >= questComponentAct.Template.Count * 3 / 2)
                return QuestObjectiveStatus.Overachieved;
            if (Template.LetItDone && Objectives[questComponentAct.Template.ThisComponentObjectiveIndex] > questComponentAct.Template.Count)
                return QuestObjectiveStatus.ExtraProgress;
            if (Objectives[questComponentAct.Template.ThisComponentObjectiveIndex] >= questComponentAct.Template.Count)
                return QuestObjectiveStatus.QuestComplete;
            if (Template.LetItDone && (Objectives[questComponentAct.Template.ThisComponentObjectiveIndex] >= questComponentAct.Template.Count * 1 / 2))
                return QuestObjectiveStatus.CanEarlyComplete;
        }

        return QuestObjectiveStatus.NotReady;
    }

    /// <summary>
    /// Returns the objective score in percent (1f = 100%)
    /// </summary>
    /// <returns></returns>
    public float GetQuestObjectivePercent()
    {
        if (!QuestSteps.TryGetValue(Step, out var currentStep))
            return 0f;

        var questComponents = currentStep.Components.Values;
        if (Template.Score > 0)
        {
            // Use Score Handler
            var score = 0;
            // Loop through all components and acts to calculate their score
            foreach (var questComponent in questComponents)
            foreach (var questComponentAct in questComponent.Acts)
                score += questComponentAct.Template.Count * Objectives[questComponentAct.Template.ThisComponentObjectiveIndex];

            // Check the score cap results
            if (Template.LetItDone && score >= Template.Score * 3 / 2)
                return Template.Score * 3f / 2f;

            return 1f / Template.Score * score;
        }

        // Check individual act counts if it's not score based
        var highest = 0f;
        foreach (var questComponent in questComponents)
        foreach (var questComponentAct in questComponent.Acts)
        {
            if (Template.LetItDone && Objectives[questComponentAct.Template.ThisComponentObjectiveIndex] >=
                questComponentAct.Template.Count * 3 / 2)
            {
                highest = Math.Max(highest, questComponentAct.Template.Count * 3f / 2f);
                continue;
            }
            highest = Math.Max(highest, 1f / questComponentAct.Template.Count * Objectives[questComponentAct.Template.ThisComponentObjectiveIndex]);
        }

        return highest;
    }
}

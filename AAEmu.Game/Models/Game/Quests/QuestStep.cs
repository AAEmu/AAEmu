using System.Collections.Generic;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// A step of the Quest
/// </summary>
public class QuestStep(QuestComponentKind step, Quest parent)
{
    /// <summary>
    /// Owning Quest object
    /// </summary>
    public Quest Parent { get; private set; } = parent;

    /// <summary>
    /// This step's QuestComponentKind
    /// </summary>
    public QuestComponentKind ThisStep { get; private set; } = step;

    /// <summary>
    /// List of components inside this step by ComponentId
    /// </summary>
    public Dictionary<uint, QuestComponent> Components { get; set; } = [];

    /// <summary>
    /// Initializes all Components and their Acts for this step
    /// </summary>
    public void InitializeStep()
    {
        // Distribute any rewards that may still be open so the pool is empty
        Parent.DistributeRewards(false);

        foreach (var questComponent in Components.Values)
            questComponent.InitializeComponent();
    }

    /// <summary>
    /// Finalize all Components and their Acts for this step
    /// </summary>
    public void FinalizeStep()
    {
        foreach (var questComponent in Components.Values)
            questComponent.FinalizeComponent();
    }

    /// <summary>
    /// Runs all Acts inside this component and grabs their result
    /// </summary>
    /// <returns>True if acts run successfully, for Progress step if all act objectives have been met</returns>
    public bool RunComponents()
    {
        var res = true;

        // Cache which components are active
        foreach (var questComponent in Components.Values)
            questComponent.IsCurrentlyActive = UnitRequirementsGameData.Instance.CanComponentRun(questComponent.Template, (BaseUnit)Parent.Owner);

        var componentsOrCheck = (Parent.Template.Selective && ThisStep == QuestComponentKind.Progress);

        if (componentsOrCheck)
        {
            // Require only one of the components to be true in the progress step is quest has selective flag
            res = false;
            foreach (var questComponent in Components.Values)
            {
                if (!questComponent.IsCurrentlyActive)
                    continue;

                var componentResult = questComponent.RunComponent();
                if (componentResult)
                    Parent.ComponentId = questComponent.Template.Id;
                res |= componentResult;
            }
        }
        else
        {
            // Requires all components to be true
            foreach (var questComponent in Components.Values)
            {
                if (!questComponent.IsCurrentlyActive)
                {
                    res = false;
                    continue;
                }

                res &= questComponent.RunComponent();
                if (res)
                    Parent.ComponentId = questComponent.Template.Id;
            }
        }

        // Override result for score quests
        if ((ThisStep == QuestComponentKind.Progress) && (Parent.Template.Score > 0))
        {
            // Validate using Score combined from all components
            var score = 0;
            foreach (var questComponent in Components.Values)
            {
                if (!questComponent.IsCurrentlyActive)
                    continue;

                foreach (var questAct in questComponent.Acts)
                    score += questAct.GetObjective(Parent) * questAct.Template.Count;
            }

            res = score >= Parent.Template.Score;
            var objectiveStatus = Parent.GetQuestObjectiveStatus();
            Parent.Status = objectiveStatus >= QuestObjectiveStatus.QuestComplete
                ? QuestStatus.Ready
                : QuestStatus.Progress;
        }
        else if ((ThisStep == QuestComponentKind.Progress) && (Parent.Template.LetItDone))
        {
            // Validate using combined from all components
            var objectiveStatus = Parent.GetQuestObjectiveStatus();
            res = objectiveStatus >= QuestObjectiveStatus.Overachieved;
            Parent.Status = objectiveStatus >= QuestObjectiveStatus.QuestComplete
                ? QuestStatus.Ready
                : QuestStatus.Progress;
        }

        // Handle Supply/Reward Distribution
        res &= Parent.DistributeRewards(true);

        // LetItBeDone type of quests, are always forced forward using the Report Acts
        if ((ThisStep == QuestComponentKind.Progress) && (Parent.Template.LetItDone))
            res = false;

        return res;
    }

    /// <summary>
    /// Sets the RequestEvaluationFlag to true signalling the server that it should check this quest's progress again
    /// </summary>
    public void RequestEvaluation()
    {
        Parent.RequestEvaluation();
    }

    /// <summary>
    /// Checks if this step has any Objectives
    /// </summary>
    /// <returns></returns>
    public bool ContainsObjectives()
    {
        // Check if Progress step actually has objectives (there are some bugs in the original DB where this step contains supply item)
        foreach (var component in Components.Values)
            foreach (var questAct in component.Acts)
                if (questAct.Template.ThisComponentObjectiveIndex != 0xFF)
                    return true;
        return false;
    }
}

using System.Collections.Generic;

using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// A step of the Quest
/// </summary>
public class QuestStep(QuestComponentKind step, Quest parent)
{
    /// <summary>
    /// Owning Quest object
    /// </summary>
    public Quest Parent { get; set; } = parent;

    /// <summary>
    /// This step's QuestComponentKind
    /// </summary>
    public QuestComponentKind ThisStep { get; set; } = step;

    /// <summary>
    /// List of components inside this step by ComponentId
    /// </summary>
    public Dictionary<uint, QuestComponent> Components { get; set; } = new();

    /// <summary>
    /// Initializes all Components and their Acts for this step
    /// </summary>
    public void InitializeStep()
    {
        // Distribute any rewards that may still be open so the pool is empty
        Parent.DistributeRewards();

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

        foreach (var questComponent in Components.Values)
            res &= questComponent.RunComponent();

        // Override result for score quests
        if ((ThisStep == QuestComponentKind.Progress) && (Parent.Template.Score > 0))
        {
            // Validate using Score combined from all components
            var score = 0;
            foreach (var questComponent in Components.Values)
            foreach (var questAct in questComponent.Acts)
                score += questAct.GetObjective(Parent) * questAct.Template.Count;
            res = score >= Parent.Template.Score;
        }
        
        // Handle Supply/Reward Distribution
        res &= Parent.DistributeRewards();

        return res;
    }
    
    /// <summary>
    /// Sets the RequestEvaluationFlag to true signalling the server that it should check this quest's progress again
    /// </summary>
    public void RequestEvaluation()
    {
        Parent.RequestEvaluation();
    }
}

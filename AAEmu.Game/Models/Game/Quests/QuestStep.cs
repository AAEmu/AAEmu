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

    public bool RunComponents()
    {
        var res = true;

        foreach (var questComponent in Components.Values)
            res &= questComponent.RunComponent();

        return res;
    }
}

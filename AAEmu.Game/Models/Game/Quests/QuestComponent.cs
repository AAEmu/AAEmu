using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Used Instance of a Quests Component
/// </summary>
public class QuestComponent(QuestStep parent, QuestComponentTemplate template) : IQuestComponent
{
    public QuestComponentTemplate Template { get; set; } = template;
    public QuestStep Parent { get; set; } = parent;
    public List<IQuestAct> Acts { get; set; } = new();

    public void InitializeComponent()
    {
        foreach (var act in Acts)
            act.Template.InitializeAction(Parent.Parent, act);
    }

    public void FinalizeComponent()
    {
        foreach (var act in Acts)
            act.Template.FinalizeAction(Parent.Parent, act);
    }

    public bool RunComponent()
    {
        var res = true;

        // Normal checks
        foreach (var questAct in Acts)
            res &= questAct.RunAct();

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

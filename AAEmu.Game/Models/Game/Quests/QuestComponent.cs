using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Used Instance of a Quests Component
/// </summary>
public class QuestComponent : IQuestComponent
{
    public QuestComponentTemplate Template { get; set; }
    public QuestStep Parent { get; set; }
    public List<IQuestAct> Acts { get; set; } = new();

    public void Add(QuestComponent component)
    {
        throw new NotImplementedException();
    }

    public void Remove(QuestComponent component)
    {
        throw new NotImplementedException();
    }

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

    public QuestComponent(QuestStep parent, QuestComponentTemplate template)
    {
        Parent = parent;
        Template = template;
    }

    public bool RunComponent()
    {
        var res = true;

        foreach (var questAct in Acts)
            res &= questAct.RunAct();

        return res;
    }
}

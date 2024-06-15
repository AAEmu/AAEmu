﻿using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
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

    /// <summary>
    /// This is set internally to cache the enabled/disabled state for this component base of it's UnitReqs
    /// </summary>
    public bool IsCurrentlyActive { get; set; } = true;

    public QuestComponent(QuestStep parent, QuestComponentTemplate template)
    {
        Parent = parent;
        Template = template;
        var actTemplateList = QuestManager.Instance.GetActsInComponent(Template.Id);
        foreach (var questActTemplate in actTemplateList)
        {
            var newAct = new QuestAct(this, questActTemplate);
            Acts.Add(newAct);
        }
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

    public bool RunComponent()
    {
        var res = true;

        // Normal checks
        foreach (var questAct in Acts)
            res &= questAct.RunAct();

        // If acts completed, handle skill and buff effects
        if (res)
        {
            Parent.Parent.UseSkillAndBuff(Template);
            Parent.Parent.SetNpcAggro(Template);
        }

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

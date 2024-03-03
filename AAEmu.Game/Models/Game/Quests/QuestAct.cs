﻿using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

public class QuestAct : IComparable<QuestAct>, IQuestAct
{
    public uint Id { get; set; }
    public uint ComponentId { get; set; }
    public uint DetailId { get; set; }
    public string DetailType { get; set; }

    public byte ThisComponentObjectiveIndex { get; set; }

    public QuestComponent QuestComponent { get; }
    public QuestActTemplate Template { get; set; }

    public QuestAct(QuestComponent parentComponent)
    {
        QuestComponent = parentComponent;
    }

    public QuestActTemplate GetTemplate()
    {
        return QuestManager.Instance.GetActTemplate(DetailId, DetailType);
    }

    public T GetTemplate<T>() where T : QuestActTemplate
    {
        return QuestManager.Instance.GetActTemplate<T>(DetailId, DetailType);
    }

    public bool Use(ICharacter character, Quest quest, int objective)
    {
        var template = QuestManager.Instance.GetActTemplate(DetailId, DetailType);
        return template.Use(character, quest, this, objective);
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    public void SetObjective(Quest quest, int value)
    {
        if (quest != null)
            quest.Objectives[ThisComponentObjectiveIndex] = value;
    }

    /// <summary>
    /// Get Current Objective Count for this Act (forwarded value from Quest)
    /// </summary>
    /// <param name="quest"></param>
    /// <returns></returns>
    public int GetObjective(Quest quest)
    {
        return quest?.Objectives[ThisComponentObjectiveIndex] ?? 0;
    }

    /// <summary>
    /// Set Current Objective Count for this Act (forwards to quest object)
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="amount"></param>
    public int AddObjective(Quest quest, int amount)
    {
        if (quest == null)
            return 0;
        quest.Objectives[ThisComponentObjectiveIndex] += amount;
        return quest.Objectives[ThisComponentObjectiveIndex];
    }

    /*
     * To sort an array
     */
    public int CompareTo(QuestAct other)
    {
        return Id.CompareTo(other.Id);
    }
}

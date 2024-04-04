﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

public interface IQuestAct
{
    QuestActTemplate Template { get; }
    uint ComponentId { get; set; }
    string DetailType { get; set; }
    public QuestComponent QuestComponent { get; }
    uint Id { get; set; }
    uint DetailId { get; }
    void SetObjective(Quest quest, int value);
    int GetObjective(Quest quest);

    int CompareTo(QuestAct other);
    QuestActTemplate GetTemplate();
    T GetTemplate<T>() where T : QuestActTemplate;
    bool Use(ICharacter character, Quest quest, int objective);
    int AddObjective(Quest quest, int amount);
    /// <summary>
    /// Execute a Act and return true if successful (early complete quests should return true if minimum is met)
    /// </summary>
    /// <returns></returns>
    bool RunAct();
}

using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

// Компонент-лист
public class QuestComponent : IQuestComponent
{
    public uint Id { get; set; }
    public QuestComponentKind KindId { get; set; }
    public QuestStep Parent { get; set; }
    public List<IQuestAct> Acts { get; set; } = new();
    public uint NextComponent { get; set; }
    public QuestNpcAiName NpcAiId { get; set; }
    public uint NpcId { get; set; }
    public uint SkillId { get; set; }
    public bool SkillSelf { get; set; }
    public string AiPathName { get; set; }
    public PathType AiPathTypeId { get; set; }
    public uint NpcSpawnerId { get; set; }
    public bool PlayCinemaBeforeBubble { get; set; }
    public uint AiCommandSetId { get; set; }
    public bool OrUnitReqs { get; set; }
    public uint CinemaId { get; set; }
    public uint BuffId { get; set; }

    public void Add(QuestComponent component)
    {
        throw new NotImplementedException();
    }

    public void Remove(QuestComponent component)
    {
        throw new NotImplementedException();
    }
    public List<bool> Execute(ICharacter character, Quest quest, int objective)
    {
        var reults = new List<bool>();
        var acts = QuestManager.Instance.GetActs(this.Id);
        foreach (var act in acts)
        {
            var res = act.Use(character, quest, objective);
            reults.Add(res);
        }
        return reults;
    }
    public QuestComponent(QuestStep parent)
    {
        Parent = parent;
    }
}

// Компонент-контейнер
public class CurrentQuestComponent : IQuestComponent
{
    private List<QuestComponent> subQuestComponents = new();

    public CurrentQuestComponent() : base()
    {
    }

    public QuestComponent GetFirstComponent()
    {
        var result = subQuestComponents.First();
        return result;
    }

    public List<QuestComponent> GetComponents()
    {
        var result = subQuestComponents;
        return result;
    }

    public int GetComponentCount()
    {
        var result = subQuestComponents.Count;
        return result;
    }

    // не работает
    public QuestComponent GetComponent(int index)
    {
        var result = subQuestComponents.Take(index) as QuestComponent;
        return result;
    }
    // не работает
    public QuestComponent GetNextComponent(int index)
    {
        var second = subQuestComponents.Skip(index).Take(index + 1) as QuestComponent; // returns 2 and 3
        return second;
    }

    public uint Id { get; set; }
    public List<QuestActTemplate> ActTemplates { get; set; }

    public void Add(QuestComponent component)
    {
        subQuestComponents.Add(component);
    }

    public void Remove(QuestComponent component)
    {
        subQuestComponents.Remove(component);
    }

    public List<bool> Execute(ICharacter character, Quest quest, int objective)
    {
        var reults = new List<bool>();

        foreach (var component in subQuestComponents)
        {
            reults.AddRange(component.Execute(character, quest, objective));
        }

        return reults;
    }
}

//// Класс для подзадачи
//public class SubQuestComponent : IQuestComponent
//{
//    public uint Id { get; set; }
//    public List<QuestActTemplate> ActTemplates { get; set; }

//    public SubQuestComponent() : base()
//    {
//    }

//    public void Add(IQuestComponent component)
//    {
//        throw new NotImplementedException();
//    }

//    public void Remove(IQuestComponent component)
//    {
//        throw new NotImplementedException();
//    }

//    public List<bool> Execute(ICharacter character, Quest quest, int objective)
//    {
//        throw new NotImplementedException();
//    }
//}


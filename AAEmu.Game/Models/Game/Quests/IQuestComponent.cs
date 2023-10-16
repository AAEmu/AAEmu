using System.Collections.Generic;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

public interface IQuestComponent
{
    public uint Id { get; set; }
    public List<QuestActTemplate> ActTemplates { get; set; }

    public void Add(QuestComponent component);
    public void Remove(QuestComponent component);
    public List<bool> Execute(ICharacter character, Quest quest, int objective);
}

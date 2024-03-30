using System.Collections.Generic;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

public interface IQuestComponent
{
    public uint Id { get; set; }

    public void Add(QuestComponent component);

    public void Remove(QuestComponent component);

    /// <summary>
    /// Initialize all Acts in this Component (register event handlers)
    /// </summary>
    public void Initialize();

    /// <summary>
    /// Finalize all Acts in this Component (un-register event handlers)
    /// </summary>
    public void DeInitialize();

    public List<bool> Execute(ICharacter character, Quest quest, int objective);
}

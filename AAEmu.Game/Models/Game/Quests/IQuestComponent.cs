using System.Collections.Generic;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

public interface IQuestComponent
{
    public QuestComponentTemplate Template { get; set; }

    /// <summary>
    /// Initialize all Acts in this Component (register event handlers)
    /// </summary>
    public void Initialize();

    /// <summary>
    /// Finalize all Acts in this Component (un-register event handlers)
    /// </summary>
    public void DeInitialize();

    /// <summary>
    /// Execute all the acts in this component and return true if successful
    /// </summary>
    /// <returns></returns>
    public bool RunComponent();
}

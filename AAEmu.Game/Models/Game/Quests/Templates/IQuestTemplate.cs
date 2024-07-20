using System.Collections.Generic;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public interface IQuestTemplate
{
    uint Id { get; set; }
    bool LetItDone { get; set; }
    byte Level { get; set; }
    bool Repeatable { get; set; }
    bool RestartOnFail { get; set; }
    bool Selective { get; set; }
    int Score { get; set; }
    bool Successive { get; set; }
    IDictionary<uint, QuestComponentTemplate> Components { get; set; }
    QuestComponentTemplate GetFirstComponent(QuestComponentKind step);
    QuestComponentTemplate[] GetComponents(QuestComponentKind step);
}

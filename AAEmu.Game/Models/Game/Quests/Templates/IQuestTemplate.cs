using System.Collections.Generic;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates
{
    public interface IQuestTemplate
    {
        uint Id { get; set; }
        bool LetItDone { get; set; }
        bool Selective { get; set; }
        int Score { get; set; }
        byte Level { get; set; }
        IDictionary<uint, QuestComponent> Components { get; set; }
        QuestComponent GetFirstComponent(QuestComponentKind step);
        QuestComponent[] GetComponents(QuestComponentKind step);
    }
}

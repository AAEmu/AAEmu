using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates;

public interface IQuestTemplate
{
    uint Id { get; set; }
    string Name { get; set; }
    uint CategoryId { get; set; }
    bool LetItDone { get; set; }
    byte Level { get; set; }
    byte MinLevel { get; set; }
    byte MaxLevel { get; set; }
    byte RaceMask { get; set; }
    bool Repeatable { get; set; }
    bool RestartOnFail { get; set; }
    bool Selective { get; set; }
    int Score { get; set; }
    bool Successive { get; set; }
    bool Translate { get; set; }
    int Priority { get; set; }
    bool OnlyOneScoreTitle { get; set; }
    bool HideChapterIndex { get; set; }
    IDictionary<uint, QuestComponentTemplate> Components { get; set; }
    bool MeetsContextRequirements(AAEmu.Game.Models.Game.Char.Character character);
    QuestComponentTemplate GetFirstComponent(QuestComponentKind step);
    QuestComponentTemplate[] GetComponents(QuestComponentKind step);
}

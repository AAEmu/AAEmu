using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests
{
    public interface IQuestAct
    {
        QuestActTemplate Template { get; }
        uint ComponentId { get; set; }
        string DetailType { get; set; }
        uint Id { get; set; }
        uint DetailId { get; }

        int CompareTo(QuestAct other);
        QuestActTemplate GetTemplate();
        T GetTemplate<T>() where T : QuestActTemplate;
        bool Use(ICharacter character, Quest quest, int objective);
    }
}

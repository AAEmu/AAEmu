using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptSphere : QuestActTemplate
{
    public uint SphereId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptSphere: SphereId {SphereId}");

        quest.QuestAcceptorType = QuestAcceptorType.Sphere;
        quest.AcceptorType = SphereId;

        return false;
    }
}

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptNpcGroup : QuestActTemplate
{
    public uint QuestMonsterGroupId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActConAcceptNpcGroup: QuestMonsterGroupId={QuestMonsterGroupId}");

        if (character.CurrentTarget is null or not Npc)
            return false;

        quest.QuestAcceptorType = QuestAcceptorType.Npc;
        if (character.CurrentTarget != null)
        {
            quest.AcceptorType = character.CurrentTarget.TemplateId;
        }

        return QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, quest.AcceptorType);
    }
}

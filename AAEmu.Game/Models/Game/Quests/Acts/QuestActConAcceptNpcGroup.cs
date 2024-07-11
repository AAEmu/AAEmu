using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptNpcGroup(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestMonsterGroupId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActConAcceptNpcGroup: QuestMonsterGroupId={QuestMonsterGroupId}");

        if (quest.Owner.CurrentTarget is null or not Npc)
            return false;

        quest.QuestAcceptorType = QuestAcceptorType.Npc;
        if (quest.Owner.CurrentTarget != null)
        {
            quest.AcceptorId = quest.Owner.CurrentTarget.TemplateId;
        }

        return QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, quest.AcceptorId);
    }
}

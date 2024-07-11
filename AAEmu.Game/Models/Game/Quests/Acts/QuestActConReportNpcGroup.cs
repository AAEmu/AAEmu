using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportNpcGroup(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActConReportNpcGroup: QuestMonsterGroupId={QuestMonsterGroupId}, UseAlias={UseAlias}, QuestActObjAliasId={QuestActObjAliasId}");

        if (quest.Owner.CurrentTarget is null or not Npc)
            return false;

        var targetId = 0u;
        if (quest.Owner.CurrentTarget != null)
        {
            targetId = quest.Owner.CurrentTarget.TemplateId;
        }

        return QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, targetId);
    }
}

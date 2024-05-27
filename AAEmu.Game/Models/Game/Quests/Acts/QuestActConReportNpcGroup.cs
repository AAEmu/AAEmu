using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConReportNpcGroup : QuestActTemplate
{
    public uint QuestMonsterGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActConReportNpcGroup: QuestMonsterGroupId={QuestMonsterGroupId}, UseAlias={UseAlias}, QuestActObjAliasId={QuestActObjAliasId}");

        if (character.CurrentTarget is null or not Npc)
            return false;

        var targetId = 0u;
        if (character.CurrentTarget != null)
        {
            targetId = character.CurrentTarget.TemplateId;
        }

        return QuestManager.Instance.CheckGroupNpc(QuestMonsterGroupId, targetId);
    }
}

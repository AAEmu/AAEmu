using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Ready: casts skill_id on the character once the component's report act has completed
/// (QuestSupplySkillRules). The act never completes the ORed Ready component by itself, and the
/// cast is recorded on the QuestAct so a later evaluation does not repeat it. The client reader
/// LoadQuestActSupplySkillDescs (x2game-dev.dll FUN_39d438a0) reads id and skill_id.
/// </summary>
public class QuestActSupplySkill(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SkillId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        if (questAct.OverrideObjectiveCompleted)
            return true;

        if (!QuestSupplySkillRules.ShouldCast(questAct.QuestComponent.OverrideObjectiveCompleted, questAct.OverrideObjectiveCompleted, SkillId))
            return false;

        var result = quest.Owner.UseSkill(SkillId, quest.Owner);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), SkillId {SkillId}, Result {result}");
        questAct.OverrideObjectiveCompleted = true;
        return true;
    }
}

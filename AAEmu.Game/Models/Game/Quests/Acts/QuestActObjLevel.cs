using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjLevel(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public byte Level { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the player has reached target level
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Level {quest.Owner.Level}/{Level}");
        var res = quest.Owner.Level >= Level;
        SetObjective(quest, res ? 1 : 0);
        return res;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnLevelUp += questAct.OnLevelUp;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnLevelUp -= questAct.OnLevelUp;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnLevelUp(IQuestAct questAct, object sender, OnLevelUpArgs args)
    {
        if (questAct.Id != ActId)
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnLevelUp: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), Level {questAct.QuestComponent.Parent.Parent.Owner.Level}/{Level}");
        var res = questAct.QuestComponent.Parent.Parent.Owner.Level >= Level;
        SetObjective(questAct, res ? 1 : 0);
    }
}

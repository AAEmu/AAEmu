using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjTalkNpcGroup(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjTalkNpcGroup");
        return false;
    }
    
    /// <summary>
    /// Checks if talked to a member of target Npc group
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcGroupId {NpcGroupId}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnTalkNpcGroupMade += questAct.OnTalkNpcGroupMade;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnTalkNpcGroupMade -= questAct.OnTalkNpcGroupMade;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnTalkNpcGroupMade(IQuestAct questAct, object sender, OnTalkNpcGroupMadeArgs args)
    {
        if ((questAct.Id != ActId) || (args.NpcGroupId != NpcGroupId))
            return;

        var player = questAct.QuestComponent.Parent.Parent.Owner;
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnTalkNpcGroupMade: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {player.Name} ({player.Id}), NpcGroupId {args.NpcGroupId}, NpcId {args.NpcId}");
        SetObjective(questAct, 1);
    }    
}

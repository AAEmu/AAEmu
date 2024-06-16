using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjExpressFire(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ExpressKeyId { get; set; }
    public uint NpcGroupId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the number of emotes have been performed on target NpcGroup 
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjExpressFire({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ExpressKeyId {ExpressKeyId}, NpcGroupId {NpcGroupId}");
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnExpressFire += questAct.OnExpressFire;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnExpressFire -= questAct.OnExpressFire;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnExpressFire(IQuestAct questAct, object sender, OnExpressFireArgs args)
    {
        if (questAct.Id != ActId)
            return;
        
        if (args.EmotionId != ExpressKeyId)
            return;
      
        if (QuestManager.Instance.CheckGroupNpc(NpcGroupId, args.NpcId))
        {
            Logger.Debug($"QuestActObjExpressFire({DetailId}).OnExpressFire: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), ExpressKeyId {ExpressKeyId}, NpcGroupId {NpcGroupId}");
            AddObjective(questAct, 1);
        }
    }
}

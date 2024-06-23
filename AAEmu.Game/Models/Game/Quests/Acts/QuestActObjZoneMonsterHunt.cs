using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjZoneMonsterHunt(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ZoneId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the target amount of enemies have been killed in the specified zone
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Zone {ZoneId}");
        return quest.Template.Score > 0 ? currentObjectiveCount * Count >= quest.Template.Score : currentObjectiveCount > Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnZoneKill += questAct.OnZoneKill;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnZoneKill -= questAct.OnZoneKill;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnZoneKill(QuestAct questAct, object sender, OnZoneKillArgs args)
    {
        if ((questAct.Id != ActId) || (args.ZoneGroupId != ZoneId))
            return;
        
        if (args.Victim is not Npc npc)
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnZoneKill(@QuestActObjZoneMonsterHunt): Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), ZoneGroupId {args.ZoneGroupId}, NpcObjId {npc.ObjId}");
        AddObjective((QuestAct)questAct, 1);
    }
}

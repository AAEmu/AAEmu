using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Progress: fire skill effect effect_id count times after accept. Sources and columns in
/// QuestEffectFireRules; the raise sites are the Skill effect loop and BuffTrigger.
/// </summary>
public class QuestActObjEffectFire(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    public uint EffectId { get; set; }
    public bool TeamShare { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug(
            "{0}({1}).RunAct: Quest {2}, Owner {3}, Effect {4}, {5}/{6}",
            QuestActTemplateName, DetailId, quest.TemplateId, quest.Owner.Name, EffectId, currentObjectiveCount, Count);
        return QuestProgressActRules.ObjectiveMet(currentObjectiveCount, Count, ParentQuestTemplate.Score);
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnEffectFire += questAct.OnEffectFire;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnEffectFire -= questAct.OnEffectFire;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnEffectFire(QuestAct questAct, object sender, OnEffectFireArgs args)
    {
        if (questAct.Template.ActId != ActId || !QuestEffectFireRules.Counts(EffectId, args.EffectId))
            return;
        AddObjective(questAct, 1);

        var owner = questAct.QuestComponent.Parent.Parent.Owner;
        if (!QuestEffectFireRules.SharesWithTeam(TeamShare, owner.Id, args.SourceCharacterId))
            return;

        // Same forwarding as QuestActObjInteraction: the team-mates' handlers see a
        // SourceCharacterId that is not their own and do not forward again.
        var team = TeamManager.Instance.GetTeamByObjId(owner.ObjId);
        if (team == null)
            return;
        foreach (var member in team.Members)
        {
            if (member?.Character == null || member.Character.Id == owner.Id)
                continue;
            member.Character.Events.OnEffectFire(sender, args);
        }
    }
}

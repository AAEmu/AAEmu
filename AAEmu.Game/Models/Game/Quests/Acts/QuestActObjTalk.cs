using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjTalk(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint NpcId { get; set; }
    public bool TeamShare { get; set; }
    /// <summary>
    /// Not sure how ItemId is supposed to work here
    /// </summary>
    public uint ItemId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement ItemId? There seems to be only one valid Act that uses this. Quest: Feigned Formalities ( 3526 )
        Logger.Debug("QuestActObjTalk");
        if (character.CurrentInteractionObject is not Npc npc)
            return false;

        return npc.TemplateId == NpcId;
    }

    /// <summary>
    /// Checks if target Npc has been talked to
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcId {NpcId}, TeamShare {TeamShare}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnTalkMade += questAct.OnTalkMade;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnTalkMade -= questAct.OnTalkMade;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnTalkMade(IQuestAct questAct, object sender, OnTalkMadeArgs args)
    {
        if ((questAct.Id != ActId) || (args.NpcId != NpcId))
            return;

        var player = questAct.QuestComponent.Parent.Parent.Owner;
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnTalkMade: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {player.Name} ({player.Id}), NpcId {args.NpcId}, Source {args.SourcePlayer.Name} ({args.SourcePlayer.Id})");
        SetObjective(questAct, 1);

        if (player.Id == args.SourcePlayer.Id)
        {
            // Handle interaction that only apply to source player
            
            // Handle Team sharing (if needed)
            if (TeamShare)
            {
                // Delegate also to other team members
                var myTeam = TeamManager.Instance.GetTeamByObjId(player.ObjId);
                foreach (var teamMember in myTeam.Members)
                {
                    // Skip self
                    if (teamMember.Character.Id == player.Id)
                        continue;
                    
                    // TODO: Range check?

                    // Directly call OnTalkMade on team members to avoid loops/duplicates
                    teamMember.Character.Events.OnTalkMade(sender, args);
                }
            }
        }
    }
}

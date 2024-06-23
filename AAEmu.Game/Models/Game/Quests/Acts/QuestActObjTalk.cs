using System;
using AAEmu.Game.Core.Managers;
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

    /// <summary>
    /// Checks if target Npc has been talked to
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcId {NpcId}, TeamShare {TeamShare}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnTalkMade += questAct.OnTalkMade;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnTalkMade -= questAct.OnTalkMade;
        base.FinalizeAction(quest, questAct);
    }

    public override void OnTalkMade(QuestAct questAct, object sender, OnTalkMadeArgs args)
    {
        if ((questAct.Id != ActId) || (args.NpcId != NpcId))
            return;

        var player = questAct.QuestComponent.Parent.Parent.Owner;
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnTalkMade: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {player.Name} ({player.Id}), NpcId {args.NpcId}, Source {args.SourcePlayer.Name} ({args.SourcePlayer.Id})");
        SetObjective((QuestAct)questAct, 1);

        if (player.Id == args.SourcePlayer.Id)
        {
            // Handle interaction that only apply to source player

            // Handle Team sharing (if needed)
            if (TeamShare)
            {
                // Delegate also to other team members
                var myTeam = TeamManager.Instance.GetTeamByObjId(player.ObjId);
                if (myTeam != null)
                {
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
}

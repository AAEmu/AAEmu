using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjInteraction(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint DoodadId { get; set; }
    public bool UseAlias { get; set; }
    public bool TeamShare { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint Phase { get; set; } // phase here is same as the related WI effect's next_phase of the doodad for the quest

    /// <summary>
    /// Checks if the number of interactions has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Count {currentObjectiveCount}/{Count}, WorldInteractionId {WorldInteractionId}, DoodadId {DoodadId}, TeamShare {TeamShare}, Phase {Phase}.");
        return currentObjectiveCount >= Count;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnInteraction += questAct.OnInteraction;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnInteraction -= questAct.OnInteraction;
        base.FinalizeAction(quest, questAct);
    }
    
    public override void OnInteraction(QuestAct questAct, object sender, OnInteractionArgs args)
    {
        if (questAct.Id != ActId)
            return;

        if (args.DoodadId != DoodadId)
            return;

        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnInteraction: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), WorldInteractionId {WorldInteractionId}, DoodadId {DoodadId}, TeamShare {TeamShare}, Phase {Phase}.");
        AddObjective((QuestAct)questAct, 1);
        
        var player = questAct.QuestComponent.Parent.Parent.Owner;
        if (player.Id == args.SourcePlayer.Id)
        {
            // Handle interaction that only apply to source player
            // TODO Verify: Is Phase here what is actually used to move the Doodad to that phase, or is it the WI that causes the change
            
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

                        // Directly call OnInteraction on team members to avoid loops/duplicates
                        teamMember.Character.Events.OnInteraction(sender, args);
                    }
                }
            }
        }
    }
}

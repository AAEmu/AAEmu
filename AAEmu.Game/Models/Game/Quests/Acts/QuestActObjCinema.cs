using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjCinema(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint CinemaId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the Cinematic has been played
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, CinemaId {CinemaId}");
        // Assume the client will actually start the cinema on its own
        return currentObjectiveCount >= 1;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnCinemaStarted += questAct.OnCinemaStarted;
        quest.Owner.Events.OnCinemaEnded += questAct.OnCinemaEnded;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
    {
        quest.Owner.Events.OnCinemaEnded -= questAct.OnCinemaEnded;
        quest.Owner.Events.OnCinemaStarted -= questAct.OnCinemaStarted;
        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Player request playing a cinematic
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender">Character</param>
    /// <param name="e">Note, owning quest is not populated here</param>
    public override void OnCinemaStarted(QuestAct questAct, object sender, OnCinemaStartedArgs e)
    {
        if (questAct.Id != ActId)
            return;

        if (sender is not Character player)
            return;
        // Set currentlyPlayingId
        player.CurrentlyPlayingCinemaId = CinemaId;
    }

    /// <summary>
    /// Player finished playing a cinematic
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender">Character</param>
    /// <param name="e">Note, owning quest is not populated here</param>
    public override void OnCinemaEnded(QuestAct questAct, object sender, OnCinemaEndedArgs e)
    {
        if (questAct.Id != ActId)
            return;

        if (sender is not Character player)
            return;
        // Playing something else?
        if (player.CurrentlyPlayingCinemaId != CinemaId)
            return;

        SetObjective((QuestAct)questAct, 1);
        player.CurrentlyPlayingCinemaId = 0;
    }
}

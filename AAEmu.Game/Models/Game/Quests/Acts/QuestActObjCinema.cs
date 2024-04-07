using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjCinema(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint CinemaId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjCinema");
        return false;
    }

    /// <summary>
    /// Checks if the Cinematic has been played
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjCinema({DetailId}).RunAct: Quest: {quest.TemplateId}, CinemaId {CinemaId}");
        // Assume the client will actually start the cinema on it's own
        return currentObjectiveCount >= 1;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnCinemaStarted += OnCinemaStarted;
        quest.Owner.Events.OnCinemaEnded += OnCinemaEnded;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnCinemaEnded -= OnCinemaEnded;
        quest.Owner.Events.OnCinemaStarted -= OnCinemaStarted;
        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Player request playing a cinematic
    /// </summary>
    /// <param name="sender">Character</param>
    /// <param name="e">Note, owning quest is not populated here</param>
    private void OnCinemaStarted(object sender, OnCinemaStartedArgs e)
    {
        if (sender is not Character player)
            return;
        // Set currently playing Id
        player.CurrentlyPlayingCinemaId = CinemaId;
    }

    /// <summary>
    /// Player finished playing a cinematic
    /// </summary>
    /// <param name="sender">Character</param>
    /// <param name="e">Note, owning quest is not populated here</param>
    private void OnCinemaEnded(object sender, OnCinemaEndedArgs e)
    {
        if (sender is not Character player)
            return;
        // Playing something else?
        if (player.CurrentlyPlayingCinemaId != CinemaId)
            return;
        SetObjective(e.OwningQuest, 1);
        player.CurrentlyPlayingCinemaId = 0;
    }
}

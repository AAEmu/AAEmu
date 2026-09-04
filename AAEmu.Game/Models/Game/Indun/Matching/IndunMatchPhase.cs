namespace AAEmu.Game.Models.Game.Indun.Matching;

public enum IndunMatchPhase
{
    Queued,
    /// <summary>The instance copy is being built. Nobody has been offered a seat in it yet.</summary>
    Preparing,
    Inviting,
    Entering,
    Done
}

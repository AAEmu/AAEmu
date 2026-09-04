namespace AAEmu.Game.Models.Game.Indun.Matching;

/// <summary>What a match waiting on its instance copy should do next.</summary>
public enum IndunPrepareOutcome
{
    /// <summary>The copy is still being built; leave the players registered.</summary>
    KeepWaiting,
    /// <summary>Raise the enter dialog for the copy.</summary>
    Offer,
    /// <summary>Move the players in without asking (invitation type Direct).</summary>
    Enter,
    /// <summary>The copy never became ready; release the match.</summary>
    GiveUp
}

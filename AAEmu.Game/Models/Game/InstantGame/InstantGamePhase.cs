namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// One match's lifecycle. Every transition is one-directional; a match that leaves a phase never
/// re-enters it, which is what makes finish/teardown idempotent and lets the manager tell a
/// still-filling match (safe to expire) from one already playing (never expire mid-match).
/// </summary>
public enum InstantGamePhase
{
    /// <summary>Created; invites open and players are still arriving.</summary>
    Filling = 0,

    /// <summary>Everyone arrived; ready hold + countdown to the opening bell.</summary>
    Opening = 1,

    /// <summary>Start sent; the playing clock runs.</summary>
    Playing = 2,

    /// <summary>Result sent; the ending hold before everyone is released.</summary>
    Ending = 3,

    /// <summary>Torn down; all per-player state released and the copy returned.</summary>
    Finished = 4,
}

namespace AAEmu.Game.Models.Game.Indun.Matching;

/// <summary>
/// An instance copy that matchmaking asked for before offering it to the players it matched, so the
/// build happens while they still sit on the registered screen instead of after they accept.
/// </summary>
public interface IPreparedIndunInstance
{
    /// <summary>The copy is built and a player entering it now goes straight in.</summary>
    bool IsReady { get; }

    /// <summary>Give up a copy that nobody is going to enter.</summary>
    void Discard();
}

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Visit counts for a self-next loop. Each chain that entered the loop has its own count,
/// so two charges that share an event id each get the event's ticket budget.
/// </summary>
public sealed class PlotTicketLedger
{
    private int _nextChain;
    private readonly Dictionary<(uint EventId, int Chain), int> _counts = [];

    public int Allocate() => ++_nextChain;

    public int NextSelf(uint eventId, int chain)
    {
        var key = (eventId, chain);
        _counts.TryGetValue(key, out var count);
        count++;
        _counts[key] = count;
        return count;
    }
}

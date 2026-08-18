namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Plot event <c>tickets</c> is a max visit count on self-next loops.
/// tickets &lt;= 0 stays uncapped. tickets == 1 on a self-next means one visit (stops a rotate
/// loop). tickets == 1 without a self-next is not a cap — several parents can enqueue the same
/// spawn event (Lusca 1827: three rings merge into one SpawnEffect node).
/// tickets &gt;= 2 always caps at that many visits.
/// </summary>
public static class PlotTicketGate
{
    public static bool IsExhausted(int visitCount, int maxTickets, bool selfLoop = false)
    {
        if (maxTickets <= 0)
            return false;
        if (maxTickets == 1 && !selfLoop)
            return false;
        return visitCount > maxTickets;
    }
}

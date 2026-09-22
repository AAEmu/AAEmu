namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// A Progress act whose subsystem the server does not have keeps its objective slot, so Accept
/// cannot walk Start to Reward, and says so once per act type instead of on every evaluation.
/// </summary>
public static class QuestUnsupportedProgressActRules
{
    private static readonly HashSet<string> Reported = [];

    public static bool FirstReport(ISet<string> reported, string actTypeName)
        => reported != null && !string.IsNullOrEmpty(actTypeName) && reported.Add(actTypeName);

    public static bool ReportOnce(string actTypeName)
    {
        lock (Reported)
            return FirstReport(Reported, actTypeName);
    }
}

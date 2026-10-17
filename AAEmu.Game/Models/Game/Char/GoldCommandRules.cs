namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Arg shaping for <c>/gold</c>. The subcommand form requires
/// <c>add|set|remove</c> plus a target token; bare amounts are rewritten to
/// <c>add self &lt;amount&gt;</c> so the common GM form works.
/// </summary>
public static class GoldCommandRules
{
    private static readonly HashSet<string> SubCommands =
        new(StringComparer.OrdinalIgnoreCase) { "add", "set", "remove", "help" };

    /// <summary>
    /// Rewrites a bare amount (or target+amount) into the subcommand shape the
    /// gold set handler expects. Returns the input unchanged when already shaped.
    /// </summary>
    public static string[] NormalizeArgs(string[] args)
    {
        if (args == null || args.Length == 0)
            return args ?? [];

        if (SubCommands.Contains(args[0]))
            return args;

        // /gold 100 [silver] [copper] → add self 100 …
        if (int.TryParse(args[0], out _))
        {
            var shaped = new string[args.Length + 2];
            shaped[0] = "add";
            shaped[1] = "self";
            Array.Copy(args, 0, shaped, 2, args.Length);
            return shaped;
        }

        // /gold Tester 100 → add Tester 100
        var withAdd = new string[args.Length + 1];
        withAdd[0] = "add";
        Array.Copy(args, 0, withAdd, 1, args.Length);
        return withAdd;
    }

    /// <summary>
    /// Copper delta for the wallet. <paramref name="mode"/> <c>set</c> uses
    /// <paramref name="desiredCopper"/> as an absolute balance; add/remove use it as a signed delta.
    /// </summary>
    public static long MoneyDelta(string mode, long desiredOrDeltaCopper, long currentCopper)
    {
        if (string.Equals(mode, "set", StringComparison.OrdinalIgnoreCase))
            return desiredOrDeltaCopper - currentCopper;
        return desiredOrDeltaCopper;
    }
}

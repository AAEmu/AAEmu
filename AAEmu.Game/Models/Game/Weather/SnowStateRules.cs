namespace AAEmu.Game.Models.Game.Weather;

/// <summary>
/// Decides the global snow state from its three sources. The configured feature bit and the
/// weather cycle can each turn snow on, and neither can turn off snow the other one holds. An
/// operator hold from <c>/snow</c> wins over both until it is released.
/// </summary>
public static class SnowStateRules
{
    /// <param name="operatorHold">Snow held on or off by <c>/snow</c>, or null when nothing is held.</param>
    /// <param name="configured">The snow feature bit from the configured feature set.</param>
    /// <param name="cycle">Whether the weather cycle currently has a snow phase open.</param>
    public static bool Effective(bool? operatorHold, bool configured, bool cycle) =>
        operatorHold ?? (configured || cycle);
}

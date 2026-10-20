namespace AAEmu.Game.Models.Game.Skills.Plots;

/// <summary>
/// How a plot-ended item cast publishes its unlock to the client.
/// </summary>
/// <remarks>
/// Equipped pieces that cast a plot skill used to send <c>ItemUnlock</c> with an <c>ItemUpdate</c>
/// (<c>UpdateDetail</c>) body. On this client that task is not a detail write — it leaves the piece
/// as a broken icon until unequip+equip. Same trap as lure recharge and bag repair. The unlock
/// therefore carries no item-task body. It also does not send <c>SkillEnded</c>.
/// </remarks>
public static class PlotItemUnlockRules
{
    /// <summary>
    /// Whether an <c>UpdateDetail</c> item task may ride on the plot's <c>ItemUnlock</c> packet.
    /// Always false — that action corrupts the client draw of the piece.
    /// </summary>
    public static bool AllowsUpdateDetailOnUnlock => false;

    /// <summary>
    /// A plot-only item cast never sends <c>SkillStarted</c>, so the client has no timeline for it.
    /// <c>SkillEnded</c> on the plot timeline is a miss: the client raises a failed skill event, which
    /// strips a hide the plot just applied.
    /// </summary>
    public static bool SendsSkillEnded => false;

    /// <summary>
    /// Whether the unlock rewrites the piece's use-skill charge clock. Writing that clock did not
    /// release the hotbar shortcut, and the item rewrite dropped a hide the plot had just applied.
    /// </summary>
    public static bool SendsChargeClock => false;
}

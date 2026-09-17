namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The per-tick mana drain of a buff: <c>buffs.tick_mana_cost</c> (a flat amount) and
/// <c>buffs.tick_level_mana_cost</c> (a multiple of the level curve the Dash drain already used). Ten
/// 10.0.2.13 rows carry one of the two columns; before this class only buff 2675 Dash was wired, through
/// a <c>BuffConstants.Dash</c> comparison in <c>BuffTemplate.Start</c>.
/// </summary>
/// <remarks>
/// Counted from the content DB (30 654 <c>buffs</c> rows): 8 rows carry <c>tick_mana_cost</c> (4 on buff
/// 108, 39 on 200, 10 on 211 and 255, 4 on 618 and 1583, 10 000 on 4140, 100 on 15786) and 2 carry
/// <c>tick_level_mana_cost</c>, both at 0.5 — Dash (2675) and 15931. No row carries both, so the two
/// columns are summed and the sum is the one that is set.
/// <para>
/// Buff 4140 is the one row that carries a cost without a tick interval (<c>tick</c> 0), and
/// <see cref="PaysPerTick"/> refuses it: a drain with no interval has nothing to pace it, and charging
/// 10 000 mana on the manager's own 200 ms clock would empty any pool in under a second. It keeps doing
/// nothing, exactly as it does today.
/// </para>
/// </remarks>
public static class TickManaCostRules
{
    /// <summary>The interval <c>ManaRegenManager</c> ticks at, in milliseconds.</summary>
    public const int ManagerTickMs = 200;

    /// <summary>
    /// Whether a buff registers a tick mana drain: one of the two cost columns has to be set, and the buff
    /// needs a tick interval (<c>buffs.tick</c>) for the manager's clock to be divided into.
    /// </summary>
    public static bool PaysPerTick(int tickManaCost, double tickLevelManaCost, double tick) =>
        (tickManaCost > 0 || tickLevelManaCost > 0) && tick > 0;

    /// <summary>The flat part of a payment; a negative column is refused rather than paying the owner.</summary>
    public static int FlatCost(int tickManaCost) => Math.Max(0, tickManaCost);

    /// <summary>
    /// How many manager ticks pass between two payments. Dash's <c>tick</c> is 200 and the manager ticks
    /// every 200 ms, so this is exactly 1 and its drain stays on every tick — the cadence it had when the
    /// manager had no cadence of its own. The 1 000 ms rows pay every fifth tick. Ties round up (500 ms
    /// pays every third 200 ms tick, not every second), so a drain is never charged faster than the buff
    /// asks for.
    /// </summary>
    public static int TicksPerPayment(double tick) =>
        Math.Max(1, (int)Math.Round(tick / ManagerTickMs, MidpointRounding.AwayFromZero));

    /// <summary>
    /// One payment: the flat column plus the level curve times <paramref name="tickLevelManaCost"/>.
    /// </summary>
    public static double Payment(int tickManaCost, double tickLevelManaCost, double levelCurveValue) =>
        FlatCost(tickManaCost) + levelCurveValue * Math.Max(0, tickLevelManaCost);
}

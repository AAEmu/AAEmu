using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// A <c>buff_effects</c> row whose <c>buff_id</c> has no <c>buffs</c> row — 41 of them in 10.0.2.13 — loads
/// without a <see cref="BuffTemplate"/>: there is nothing for it to apply, tick or report.
/// </summary>
/// <remarks>
/// The row is still registered under its own id, because three other tables resolve it through the shared
/// effect-id table before any of them look at the buff: 19 enabled <c>skill_effects</c> (10580, 11454,
/// 11426, 12841, 14600, 16927, 17365, 18447, 20295, 20364, 20536, 22122, 19238, 27769, 28698, 28699,
/// 28700, 25000, 39574), 2 enabled <c>buff_triggers</c> and 2 <c>buff_tick_effects</c>. Dropping the row
/// would hand all of those a null template instead — and <see cref="BuffTemplate.DoAreaTick"/> has no null
/// check on <c>GetEffectTemplate</c>, which buff 70 (tick_area_radius 15, tick effect 2710 → row 824)
/// reaches. These rules make the templateless row inert instead, so nothing dereferences a null
/// <c>Buff</c>.
/// </remarks>
public static class BuffEffectDispatchRules
{
    /// <summary>With no buff template there is nothing to apply to the target.</summary>
    public static bool IsDispatchable(BuffTemplate buff) => buff != null;

    /// <summary>
    /// The buff id a templateless effect reports: 0, the "no buff" id every consumer already filters on
    /// (<c>Buffs.CheckBuff</c> / <c>RemoveBuff</c>, <c>AreaTrigger</c>, <c>PlotEventEffect</c>). Reporting
    /// the effect's own id would name a buff that exists in no table.
    /// </summary>
    public static uint ResolveBuffId(BuffTemplate buff) => buff?.Id ?? 0;

    /// <summary>A templateless effect has no duration, so it never ticks.</summary>
    public static bool HasTick(BuffTemplate buff) => buff is { Tick: > 0 };
}

using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The pure part of <see cref="AggroEffect"/>: who may raise aggro and on whom.
/// </summary>
/// <remarks>
/// The effect used to require a <c>Character</c> caster, so the 353 <c>buff_triggers</c> rows that carry an
/// <c>AggroEffect</c> — every one of them cast by an Npc, a mate or a slave — rolled their value and then
/// dropped it. The caster's class is not what decides whether a threat value is meaningful: the unit that
/// holds the aggro table is, and only an <see cref="Npc"/> has one.
/// </remarks>
public static class AggroEffectRules
{
    /// <summary>
    /// Whether <paramref name="caster"/> can raise aggro at all: it has to be a spawned unit, because the
    /// value is attributed to an object id.
    /// </summary>
    public static bool CanCast(BaseUnit caster) => caster is Unit { ObjId: > 0 };

    /// <summary>
    /// Whether <paramref name="target"/> holds an aggro table this effect can add to. A player, a mate, a
    /// slave and a gimmick have none.
    /// </summary>
    public static bool CanHoldAggro(BaseUnit target) => target is Npc { ObjId: > 0 };
}

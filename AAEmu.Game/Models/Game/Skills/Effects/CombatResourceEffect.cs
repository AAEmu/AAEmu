using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// combat_resource_effects — grants/consumes a v10 combat resource (combo-point style resource) on the unit.
public class CombatResourceEffect : EffectTemplate
{
    public int MinCombatResource { get; set; }
    public int MaxCombatResource { get; set; }
    public int CombatResourceId { get; set; }
    public int Chance { get; set; }
    public bool ResetRemainTime { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // TODO: roll Chance and add a CombatResourceId amount in [Min,Max] to the unit, optionally resetting its
        // remain timer. The v10 combat-resource system is not modeled server-side yet.
    }
}

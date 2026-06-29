using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// char_transform_effects — toggles a visual race/gender transform on the unit (costume/polymorph forms).
public class CharTransformEffect : EffectTemplate
{
    public int CharRaceId { get; set; }
    public int CharGenderId { get; set; }
    public bool IsTransform { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // TODO: swap the target's visual model to CharRaceId/CharGenderId when IsTransform, and restore it
        // otherwise, broadcasting the appearance change. Runtime model transforms are not wired yet.
    }
}

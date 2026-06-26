using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillControllerTemplate : EffectTemplate
{
    public uint KindId { get; set; }
    public int[] Value { get; set; } = new int[15];

    public byte ActiveWeaponId { get; set; }
    public uint EndSkillId { get; set; } // 10.0.2.13: skill_controllers.end_skill_id present again
    public override bool OnActionTime { get; }

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj,
        EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("SkillControllerTemplate");
    }
}

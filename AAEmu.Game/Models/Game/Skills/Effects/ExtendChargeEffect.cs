using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// extend_charge_effects — extends a charge skill's accumulated damage/charge from the configured sources
// (fixed / percent / level / dps / source-health), optionally granting a charge buff.
public class ExtendChargeEffect : EffectTemplate
{
    public int DamageTypeId { get; set; }
    public bool UseFixedCharge { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public bool UsePercentCharge { get; set; }
    public int PercentMin { get; set; }
    public int PercentMax { get; set; }
    public bool UseLevelCharge { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool UseDpsCharge { get; set; }
    public float DpsIncMultiplier { get; set; }
    public bool UseMainhandWeapon { get; set; }
    public bool UseOffhandWeapon { get; set; }
    public bool UseRangedWeapon { get; set; }
    public float DpsMultiplier { get; set; }
    public int ChargeBuffId { get; set; }
    public int PercentDamageResourceTypeId { get; set; }
    public bool UseSourceHealth { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        // TODO: accumulate the extended charge per the enabled sources and apply ChargeBuffId. Charge-skill
        // accumulation is not modeled server-side yet.
    }
}

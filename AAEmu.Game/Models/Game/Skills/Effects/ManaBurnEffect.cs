using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class ManaBurnEffect : EffectTemplate
{
    public int BaseMin { get; set; }
    public int BaseMax { get; set; }
    public int DamageRatio { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool UseFixedCharge { get; set; }
    public bool UsePercentCharge { get; set; }
    public int PercentMin { get; set; }
    public int PercentMax { get; set; }
    public bool UseLevelCharge { get; set; }
    public int DamageTypeId { get; set; }
    public float DpsIncMultiplier { get; set; }
    public bool UseMainhandWeapon { get; set; }
    public bool UseOffhandWeapon { get; set; }
    public bool UseRangedWeapon { get; set; }
    public float DpsMultiplier { get; set; }
    public float ManaDrainRatio { get; set; }
    public int PercentDamageResourceTypeId { get; set; }
    public bool UseSourceHealth { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("ManaBurnEffect");

        // buffs.mana_burn_immune (338 rows) is read off the target's active buffs, the same way
        // CheckDamageImmune reads its flags. Checked before the roll so an immune target does not
        // consume a damage roll it never sees.
        if (target is Unit immuneTarget && immuneTarget.Buffs.CheckManaBurnImmune())
        {
            Logger.Debug("ManaBurnEffect refused on {0}: mana_burn_immune", immuneTarget.ObjId);
            return;
        }

        if (caster is not Unit casterUnit || target is not Unit targetUnit)
            return;

        // The charge is composed from the sources the row enables, the same shape the damage and heal effects
        // use. use_level_charge was the one that mattered: the level term used to run unconditionally, so the
        // 24 rows that turn it off burned a level rating they never authored (all 24 ship level_md 1.0, the
        // table default).
        var fixedCharge = ManaBurnRules.FixedCharge(
            UseFixedCharge, BaseMin, BaseMax, Random.Shared.Next(0, Math.Max(BaseMin, BaseMax) + 1));

        var percentRoll = PercentMin == PercentMax
            ? PercentMin
            : Random.Shared.Next(Math.Min(PercentMin, PercentMax), Math.Max(PercentMin, PercentMax) + 1);
        var resource = (ManaBurnRules.ChargeResource)PercentDamageResourceTypeId;
        var percentCharge = ManaBurnRules.PercentCharge(resource, percentRoll, PoolOf(resource, targetUnit));

        var levelCharge = ManaBurnRules.LevelCharge(
            UseLevelCharge, LevelMd, casterUnit.LevelDps, source?.Skill?.Level ?? 1, LevelVaStart, LevelVaEnd);

        var charge = ManaBurnRules.TotalCharge(fixedCharge, percentCharge, levelCharge);

        if (source?.Buff?.TickEffects.Count > 0 && source.Buff.Duration != 0)
            charge = (int)(charge * (source.Buff.Tick / (double)source.Buff.Duration));

        if (charge <= 0)
            return;

        // damage_ratio (38 rows non-zero: 10000 on 28, 500 on 9, 15000 on 1) is per ten thousand: the share
        // of the burned mana that lands as health damage. 0 on the other 62 rows is "mana only", so those
        // leave health alone exactly as they did.
        var healthDamage = ManaBurnRules.HealthDamage(charge, DamageRatio);

        targetUnit.ReduceCurrentMp(caster, charge);
        if (healthDamage > 0)
            targetUnit.ReduceCurrentHp(caster, healthDamage);

        var packet = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, healthDamage, 0)
        {
            _manaBurn = charge
        };
        target.BroadcastPacket(packet, true);
    }

    private static int PoolOf(ManaBurnRules.ChargeResource resource, Unit unit) => resource switch
    {
        ManaBurnRules.ChargeResource.CurrentHealth => unit.Hp,
        ManaBurnRules.ChargeResource.MaxHealth => unit.MaxHp,
        ManaBurnRules.ChargeResource.CurrentMana => unit.Mp,
        ManaBurnRules.ChargeResource.MaxMana => unit.MaxMp,
        _ => 0
    };
}

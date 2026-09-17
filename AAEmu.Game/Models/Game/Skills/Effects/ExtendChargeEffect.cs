using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// extend_charge_effects — adds charge to an absorption buff from the configured sources (fixed / percent /
// level / dps), optionally granting that buff first. The 23 rows name 14 different shields and every one of
// them is a 보호막 whose damage_absorption_type_id is 2, which is the charge Unit.ReduceCurrentHp spends.
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
        Logger.Trace("ExtendChargeEffect {0}", Id);

        if (caster is not Unit casterUnit || target is not Unit targetUnit)
            return;

        if (ChargeBuffId <= 0)
            return;

        var added = ComposeCharge(casterUnit, targetUnit, source);
        if (added <= 0)
            return;

        var live = targetUnit.Buffs.GetEffectFromBuffId((uint)ChargeBuffId);
        if (live == null)
        {
            // The named shield is not up, and these rows are the only thing that can put it up: no
            // skill_effects row anywhere applies these 14 buffs through a BuffEffect, so without this the
            // shield never exists and its 14 skills (10153 보호막, 39887 개인 보호막, 42784 정원의 가호,
            // 43207 모두 치유: 파도 …) do nothing at all. The composed charge is carried onto the instance so
            // BuffTemplate.Start does not roll the template's own init_min/init_max range on top of it —
            // skill 10153's text is "보호량: … + 자신의 최대 활력 5%", and buff 95's authored range is
            // 1-15,000, which would otherwise be what the shield starts at.
            GrantShield(targetUnit, casterUnit, added);
            live = targetUnit.Buffs.GetEffectFromBuffId((uint)ChargeBuffId);
            if (live == null)
            {
                Logger.Debug("ExtendChargeEffect {0}: buff {1} could not be applied to {2}",
                    Id, ChargeBuffId, targetUnit.ObjId);
                return;
            }
        }
        else
        {
            // AddCharge notifies the client and the zone; the resulting charge is read back off the instance
            // rather than assigned, so ExtendedCharge is the only place the ceiling is decided.
            live.AddCharge(ExtendChargeRules.ExtendedCharge(
                live.Charge, added, live.Template?.MaxCharge ?? 0));
        }

        Logger.Debug("ExtendChargeEffect {0}: buff {1} on {2} +{3} -> {4}",
            Id, ChargeBuffId, targetUnit.ObjId, added, live.Charge);
    }

    private void GrantShield(Unit targetUnit, Unit caster, int charge)
    {
        var template = SkillManager.Instance.GetBuffTemplate((uint)ChargeBuffId);
        if (template == null)
            return;

        var buff = new Buff(targetUnit, caster, new SkillCasterUnit(caster.ObjId), template, null, DateTime.UtcNow)
        {
            Charge = charge
        };
        targetUnit.Buffs.AddBuff(buff);
    }

    private int ComposeCharge(Unit casterUnit, Unit targetUnit, EffectSource source)
    {
        var fixedRoll = FixedMin == FixedMax
            ? FixedMin
            : Random.Shared.Next(Math.Min(FixedMin, FixedMax), Math.Max(FixedMin, FixedMax) + 1);

        var percentRoll = PercentMin == PercentMax
            ? PercentMin
            : Random.Shared.Next(Math.Min(PercentMin, PercentMax), Math.Max(PercentMin, PercentMax) + 1);

        // use_source_health (4 rows) picks whose pool the percentage comes off. Those four are the raid-wide
        // 모두 치유: 파도 shields, where the caster and the shielded unit can be different units; the rest read
        // the caster's own pool.
        var poolOwner = UseSourceHealth ? targetUnit : casterUnit;
        var percentCharge = ExtendChargeRules.PercentCharge(
            (ExtendChargeRules.ChargeResource)PercentDamageResourceTypeId,
            percentRoll,
            PoolOf((ExtendChargeRules.ChargeResource)PercentDamageResourceTypeId, poolOwner));

        var levelCharge = ExtendChargeRules.LevelCharge(
            LevelMd, casterUnit.LevelDps, source?.Skill?.Level ?? 1, LevelVaStart, LevelVaEnd);

        var dpsCharge = ExtendChargeRules.DpsCharge(DpsIncMultiplier, SelectDpsInc(casterUnit), DpsMultiplier,
            SelectWeaponDps(casterUnit));

        return ExtendChargeRules.TotalCharge(
            UseFixedCharge, fixedRoll,
            UsePercentCharge, percentCharge,
            UseLevelCharge, levelCharge,
            UseDpsCharge, dpsCharge);
    }

    private static int PoolOf(ExtendChargeRules.ChargeResource resource, Unit unit) => resource switch
    {
        ExtendChargeRules.ChargeResource.CurrentHealth => unit.Hp,
        ExtendChargeRules.ChargeResource.MaxHealth => unit.MaxHp,
        ExtendChargeRules.ChargeResource.CurrentMana => unit.Mp,
        ExtendChargeRules.ChargeResource.MaxMana => unit.MaxMp,
        _ => 0
    };

    /// <summary>
    /// The caster stat the row's <c>damage_type_id</c> selects, the same mapping <c>DamageEffect</c> uses.
    /// </summary>
    private int SelectDpsInc(Unit casterUnit) => (DamageType)DamageTypeId switch
    {
        DamageType.Melee => casterUnit.DpsInc,
        DamageType.Magic => casterUnit.MDps + casterUnit.MDpsInc,
        DamageType.Ranged => casterUnit.RangedDpsInc,
        DamageType.Siege => casterUnit.SiegeDps,
        _ => 0
    };

    /// <summary>
    /// The equipped weapon's own DPS for the flags the row enables. All three flags are false on all 23
    /// shipped rows, so this is 0 in shipped content; it is composed for the rows a future client adds.
    /// </summary>
    private float SelectWeaponDps(Unit casterUnit)
    {
        var weaponDps = 0f;

        if (UseMainhandWeapon)
            weaponDps += WeaponDpsInSlot(casterUnit, EquipmentItemSlot.Mainhand);
        if (UseOffhandWeapon)
            weaponDps += WeaponDpsInSlot(casterUnit, EquipmentItemSlot.Offhand);
        if (UseRangedWeapon)
            weaponDps += WeaponDpsInSlot(casterUnit, EquipmentItemSlot.Ranged);

        return weaponDps;
    }

    /// <summary>
    /// The DPS of the weapon in <paramref name="slot"/>, or 0 when the slot holds no weapon. It is the
    /// weapon's own rating, not the unit's composed <c>Dps</c> attribute, which folds in strength.
    /// </summary>
    private static float WeaponDpsInSlot(Unit casterUnit, EquipmentItemSlot slot) =>
        (casterUnit.Equipment?.GetItemBySlot((int)slot) as Weapon)?.Dps ?? 0f;
}

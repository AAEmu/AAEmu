using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class HealEffect : EffectTemplate
{
    public bool UseFixedHeal { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public bool UseLevelHeal { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public bool Percent { get; set; }
    public bool UseChargedBuff { get; set; }
    public uint ChargedBuffId { get; set; }
    public float ChargedMul { get; set; }
    public bool SlaveApplicable { get; set; }
    public bool IgnoreHealAggro { get; set; }
    public float DpsMultiplier { get; set; }
    /// <summary><c>heal_effects.self_target_multiplier</c> — see <see cref="HealEffectRules"/>.</summary>
    public float SelfTargetMul { get; set; }
    public uint ActabilityGroupId { get; set; }
    public int ActabilityStep { get; set; }
    public float ActabilityMul { get; set; }
    public float ActabilityAdd { get; set; }

    /// <summary>
    /// The <c>unit_modifiers</c> rows this effect owns (owner_type='HealEffect'), attached at load the way
    /// <see cref="DamageEffect.Bonuses"/> is. See <see cref="HealEffectRules"/>.
    /// </summary>
    public List<BonusTemplate> Bonuses { get; set; } = [];

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("HealEffect {0}", Id);

        if (target is not Unit)
            return;
        var trg = (Unit)target;

        if (trg.Hp <= 0)
            return;

        var min = 0.0f;
        var max = 0.0f;

        var levelMin = 0.0f;
        var levelMax = 0.0f;

        if (UseLevelHeal)
        {
            var lvlMd = ((Unit)caster).LevelDps * LevelMd;
            var levelModifier = (((source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;

            levelMin += lvlMd - levelModifier * lvlMd + 0.5f;
            levelMax += (levelModifier + 1) * lvlMd + 0.5f;
        }

        max += ((Unit)caster).HDps * 0.001f * DpsMultiplier;

        var minCastBonus = 1000f;
        // Hack null-check on skill
        var castTimeMod = source.Skill?.Template.CastingTime ?? 0; // This mod depends on casting_inc too!
        if (castTimeMod <= 1000)
            minCastBonus = min > 0 ? min : minCastBonus;
        else
            minCastBonus = castTimeMod;

        var variableDamage = max * minCastBonus * 0.001f;
        min = variableDamage + levelMin;
        max = variableDamage + levelMax;

        // The caster's skill_modifiers heal rows (attribute 12, authored as a per-cent delta: the shipped
        // rows are 7-20), applied to the composed heal exactly as DamageEffect applies SkillAttribute.Damage
        // to its composed min/max. No such row leaves the heal bit-for-bit what it was.
        if (source.Skill != null)
        {
            min = (float)caster.SkillModifiersCache.ApplyModifiers(source.Skill, SkillAttribute.Heal, min);
            max = (float)caster.SkillModifiersCache.ApplyModifiers(source.Skill, SkillAttribute.Heal, max);
        }

        var tickModifier = 1.0f;
        if (source.Buff?.TickEffects.Count > 0 && source.Buff.Duration != 0)
        {
            tickModifier = (float)(source.Buff.Tick / source.Buff.Duration);
        }

        min *= tickModifier;
        max *= tickModifier;

        // The healer's own heal output (unit_modifiers 222, heal_damage_mul), on the composed heal the
        // way DamageEffect applies the attacker's damage multipliers to its composed min/max. It has no
        // victim-kind split and is not IncomingHealMul, which is the healed unit's attribute and is
        // applied further down. No such bonus on the caster means exactly 1.0f here and nothing changes.
        var healDamageMultiplier = ((Unit)caster).HealDamageMul;
        min *= healDamageMultiplier;
        max *= healDamageMultiplier;

        if (UseChargedBuff)
        {
            var effect = caster.Buffs.GetEffectFromBuffId(ChargedBuffId);
            if (effect != null)
            {
                min += ChargedMul * effect.Charge;
                max += ChargedMul * effect.Charge;
                effect.Exit();
            }
        }

        // The effect's own heal_critical_mul row (185). 158 of the 160 HealEffect rows carry -2000, which
        // lands on a multiplier of -1: those heals never roll a critical. No row leaves 1.0, and the branch
        // below is then exactly what it was.
        var criticalMultiplier = HealEffectRules.CriticalMultiplier(Bonuses);
        var criticalHeal = HealEffectRules.CanCrit(criticalMultiplier)
                           && Random.Shared.Next(0f, 100f) < ((Unit)caster).HealCritical;

        var value = (int)Random.Shared.Next(min, max);

        // percent (237 rows): the row heals a share of the healed unit's maximum instead of the
        // absolute composition above. It replaces that composition — the authored min/max and the
        // level, DPS, heal skill_modifier and ChargedMul terms all go with it — but it is taken here,
        // where the composition lands, so everything below still applies: the critical roll,
        // trg.IncomingHealMul, SelfTargetMultiplier, the caster's HealMul and the actability steps.
        // Taking it later drops healing reduction, which would make a percent heal the one heal an
        // anti-heal debuff cannot touch, and leaves a critical percent heal reporting
        // CriticalHealHit while paying an unmodified share.
        if (Percent)
        {
            value = HealEffectRules.PercentAmount(trg.MaxHp, FixedMin, FixedMax, Random.Shared.Next(0, 101));
        }

        if (criticalHeal)
        {
            value = (int)(value * (1 + ((Unit)caster).HealCriticalBonus / 100));
            if (criticalMultiplier != 1d)
                value = (int)(value * criticalMultiplier);
            caster.CombatBuffs.TriggerCombatBuffs((Unit)caster, trg, SkillHitType.SpellCritical, true, source?.Skill);
        }

        value = (int)(value * trg.IncomingHealMul);

        // Every percent row carries use_fixed_heal as well, so the two arms stay exclusive: the share
        // taken above is the whole of what a percent row heals.
        if (!Percent && UseFixedHeal)
        {
            value = Random.Shared.Next(FixedMin, FixedMax);
            if (source.Buff != null && source.IsTrigger)
            {
                value = (int)(value / 1000.0f * source.Amount);
            }
            else
                value = (int)(value * tickModifier);
        }

        // self_target_multiplier (152 rows at 0.7): a self-heal pays out less for the flagged rows. It is
        // exactly 1.0 for every other target, so this is a no-op on all but those 152 rows' self-casts.
        value = (int)(value * HealEffectRules.SelfTargetMultiplier(
            SelfTargetMul, HealEffectRules.IsSelfTarget(caster.ObjId, trg.ObjId)));

        value = (int)(value * ((Unit)caster).HealMul);

        // Check if Healing is based on proficiency
        if (caster is Character player && ActabilityGroupId > 0)
        {
            // Bonus effect based on skill level
            if (player.Actability.Actabilities.TryGetValue(ActabilityGroupId, out var actability))
            {
                var steps = actability.Point / ActabilityStep;
                if (ActabilityAdd != 0f)
                    value += (int)(steps * ActabilityAdd);
                if (ActabilityMul != 0f)
                    value += (int)(value * (float)steps * ActabilityMul);
            }
        }

        var healHitType = criticalHeal ? HealHitType.CriticalHealHit : HealHitType.HealHit;

        var packet = new SCUnitHealedPacket(castObj, casterObj, target.ObjId, HealType.Health, healHitType, value);
        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            trg.BroadcastPacket(packet, true);

        var oldHp = trg.Hp;
        trg.Hp += value;
        trg.Hp = Math.Min(trg.Hp, trg.MaxHp);
        trg.BroadcastPacket(new SCUnitPointsPacket(trg.ObjId, trg.Hp, trg.Mp), true);

        if (WorldIntegration.ZoneAuthority && packetBuilder == null)
        {
            var inCharge = caster is Unit u && u.ObjId != 0 ? u.ObjId : trg.ObjId;
            WorldIntegration.RelayUnitHealedToZone?.Invoke(
                castObj, casterObj, trg.ObjId, HealType.Health, healHitType, value, inCharge, criticalHeal);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(trg.ObjId, trg.Hp, trg.Mp);
        }

        // PvP assist tracking: a healer on the target's side earns assist credit
        // for the target's next PvP kill (within 30s window).
        if (caster is Character healerChar && trg is Character targetChar &&
            healerChar.Id != targetChar.Id && targetChar.IsInBattle)
        {
            targetChar.RecordPvpHealFrom(healerChar);
        }

        trg.Events.OnHealed(this, new OnHealedArgs
        {
            Healer = (Unit)caster,
            HealAmount = value,
            IgnoreHealAggro = IgnoreHealAggro
        });
        trg.PostUpdateCurrentHp(trg, oldHp, trg.Hp, KillReason.Unknown);

        // hit_heal / hit_heal_crit item procs (enum_proc_chance_type 17 and 18, 10 rows) on the healer, once the
        // heal has landed. The healed unit is the other side of the event.
        ((Unit)caster).Procs?.OnHeal(criticalHeal, trg, source?.Skill);
    }
}

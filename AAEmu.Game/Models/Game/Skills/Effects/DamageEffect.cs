using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class DamageEffect : EffectTemplate
{
    public DamageType DamageType { get; set; }
    public int FixedMin { get; set; }
    public int FixedMax { get; set; }
    public float Multiplier { get; set; }
    public bool UseMainhandWeapon { get; set; }
    public bool UseOffhandWeapon { get; set; }
    public bool UseRangedWeapon { get; set; }
    public int CriticalBonus { get; set; }
    public uint TargetBuffTagId { get; set; }
    public int TargetBuffBonus { get; set; }
    public bool UseFixedDamage { get; set; }
    public bool UseLevelDamage { get; set; }
    public float LevelMd { get; set; }
    public int LevelVaStart { get; set; }
    public int LevelVaEnd { get; set; }
    public float TargetBuffBonusMul { get; set; }
    public bool UseChargedBuff { get; set; }
    public uint ChargedBuffId { get; set; }
    public float ChargedMul { get; set; }
    public float AggroMultiplier { get; set; }
    public int HealthStealRatio { get; set; }
    public int ManaStealRatio { get; set; }
    public float DpsMultiplier { get; set; }
    public int WeaponSlotId { get; set; }
    public bool CheckCrime { get; set; }
    public uint HitAnimTimingId { get; set; }
    public bool UseTargetChargedBuff { get; set; }
    public uint TargetChargedBuffId { get; set; }
    public float TargetChargedMul { get; set; }
    public float DpsIncMultiplier { get; set; }
    public bool EngageCombat { get; set; }
    public bool Synergy { get; set; }
    public uint ActabilityGroupId { get; set; }
    public int ActabilityStep { get; set; }
    public float ActabilityMul { get; set; }
    public float ActabilityAdd { get; set; }
    public float ChargedLevelMul { get; set; }
    public bool AdjustDamageByHeight { get; set; }
    public bool UsePercentDamage { get; set; }
    public int PercentMin { get; set; }
    public int PercentMax { get; set; }
    public bool UseCurrentHealth { get; set; }
    public int TargetHealthMin { get; set; }
    public int TargetHealthMax { get; set; }
    public float TargetHealthMul { get; set; }
    public int TargetHealthAdd { get; set; }
    public bool FireProc { get; set; }
    public List<BonusTemplate> Bonuses { get; set; } = [];

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("DamageEffect");

        var trg = target as Unit;
        if (trg == null || trg.Hp <= 0)
        {
            return;
        }

        if (Bonuses != null)
        {
            foreach (var bonus in Bonuses)
            {
                caster.AddBonus(uint.MaxValue, new Bonus { Template = bonus, Value = bonus.Value });
            }
        }

        trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AttackedEtc);
        caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AttackEtc);

        if (target.Buffs.CheckDamageImmune(DamageType))
        {
            target.BroadcastPacket(new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, 1, 0)
            {
                HitType = SkillHitType.Immune
            }, false);
            return;
        }

        var weapon = ((Unit)caster).Equipment.GetItemBySlot(WeaponSlotId);
        var holdable = (WeaponTemplate)weapon?.Template;

        var hitType = SkillHitType.Invalid;
        if ((source?.Skill?.HitTypes.TryGetValue(trg.ObjId, out hitType) ?? false)
            && (source?.Skill.SkillMissed(trg.ObjId) ?? false))
        {
            var missPacket = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, 0, 0)
            {
                HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
                HitType = hitType
            };
            // TODO: Gotta figure out how to tell if it should be applied on getting hit, or on hitting
            trg.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false);
            caster.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false);
            caster.BroadcastPacket(missPacket, true);
            return;
        }

        float flexibilityRateMod = trg.Flexibility / 1000 * 3;
        switch (DamageType)
        {
            case DamageType.Melee:
                if (Rand.Next(0f, 100f) < ((Unit)caster).MeleeCritical - flexibilityRateMod)
                    hitType = SkillHitType.MeleeCritical;
                else
                    hitType = SkillHitType.MeleeHit;
                break;
            case DamageType.Magic:
                if (Rand.Next(0f, 100f) < ((Unit)caster).SpellCritical - flexibilityRateMod)
                    hitType = SkillHitType.SpellCritical;
                else
                    hitType = SkillHitType.SpellHit;
                break;
            case DamageType.Ranged:
                if (Rand.Next(0f, 100f) < ((Unit)caster).RangedCritical - flexibilityRateMod)
                    hitType = SkillHitType.RangedCritical;
                else
                    hitType = SkillHitType.RangedHit;
                break;
            case DamageType.Siege:
                hitType = SkillHitType.RangedHit;//No siege type?
                break;
            default:
                hitType = SkillHitType.Invalid;
                break;
        }

        var min = 0.0f;
        var max = 0.0f;

        // Used for NPCs, I think
        var levelMin = 0.0f;
        var levelMax = 0.0f;
        if (UseLevelDamage)
        {
            var lvlMd = ((Unit)caster).LevelDps * LevelMd;
            // Hack null-check on skill
            var levelModifier = (((source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;

            levelMin += lvlMd - levelModifier * lvlMd + 0.5f;
            levelMax += (levelModifier + 1) * lvlMd + 0.5f;
        }

        // Stats/Weapon DPS
        var dpsInc = 0;
        switch (DamageType)
        {
            case DamageType.Melee:
                dpsInc = ((Unit)caster).DpsInc;
                break;
            case DamageType.Magic:
                dpsInc = ((Unit)caster).MDps + ((Unit)caster).MDpsInc;
                break;
            case DamageType.Ranged:
                dpsInc = ((Unit)caster).RangedDpsInc;
                break;
        }

        max += dpsInc * 0.001f * DpsIncMultiplier;
        var weaponDamage = 0.0f;

        if (UseMainhandWeapon)
            weaponDamage = ((Unit)caster).Dps * 0.001f; // TODO : Use only weapon value!
        if (UseOffhandWeapon)
            weaponDamage = ((Unit)caster).OffhandDps * 0.001f + weaponDamage;
        if (UseRangedWeapon)
            weaponDamage = ((Unit)caster).RangedDps * 0.001f + weaponDamage; // TODO : Use only weapon value!

        max = DpsMultiplier * weaponDamage + max;

        var minCastBonus = 1000f;
        // Hack null-check on skill
        var castTimeMod = source.Skill?.Template.CastingTime ?? 0; // This mod depends on casting_inc too!
        if (castTimeMod <= 1000)
            minCastBonus = min > 0 ? min : minCastBonus;
        else
            minCastBonus = castTimeMod;

        var variableDamage = max * minCastBonus * 0.001f;
        // TODO : Handle NPC
        if (WeaponSlotId < 0)
        {
            min = variableDamage + levelMin;
            max = variableDamage + levelMax;
        }
        else
        {
            if (weapon != null)
            {
                var scaledDamage = holdable.HoldableTemplate.DamageScale * variableDamage * 0.01f;
                min = levelMin + (variableDamage - scaledDamage);
                max = levelMax + (variableDamage + scaledDamage);
            }
        }

        min *= Multiplier;
        max *= Multiplier;

        var damageMultiplier = DamageType switch
        {
            DamageType.Melee => ((Unit)caster).MeleeDamageMul,
            DamageType.Magic => ((Unit)caster).SpellDamageMul,
            DamageType.Ranged => ((Unit)caster).RangedDamageMul,
            DamageType.Siege => 1.0f, // TODO
            _ => 1f
        };

        min = MathF.Floor(min * damageMultiplier);
        max = MathF.Ceiling(max * damageMultiplier);

        if (source.Skill != null)
        {
            min = (float)caster.SkillModifiersCache.ApplyModifiers(source.Skill, SkillAttribute.Damage, min);
            max = (float)caster.SkillModifiersCache.ApplyModifiers(source.Skill, SkillAttribute.Damage, max);
        }

        if (source.Buff?.TickEffects.Count > 0)
        {
            if (source.Buff.Duration != 0)
            {
                min = (float)(min * (source.Buff.Tick / source.Buff.Duration));
                max = (float)(max * (source.Buff.Tick / source.Buff.Duration));
            }

            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageEtcDot);
            trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedEtcDot);

            if (DamageType == DamageType.Magic)
            {
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageSpellDot);
                trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedSpellDot);
            }
        }

        if (UseChargedBuff && source.Skill != null)
        {
            var effect = caster.Buffs.GetEffectFromBuffId(ChargedBuffId);
            var charges = effect?.Charge ?? 0;

            min += charges * (ChargedMul + source.Skill.Level * ChargedLevelMul);
            max += charges * (ChargedMul + source.Skill.Level * ChargedLevelMul);
            effect?.Exit();
        }

        if (UseTargetChargedBuff && source.Skill != null)
        {
            var effect = target.Buffs.GetEffectFromBuffId(ChargedBuffId);
            var charges = effect?.Charge ?? 0;

            min += charges * (ChargedMul + source.Skill.Level * ChargedLevelMul);
            max += charges * (ChargedMul + source.Skill.Level * ChargedLevelMul);
            effect?.Exit();
        }

        if (UseFixedDamage)
        {
            min = FixedMin;
            max = FixedMax;
        }

        var finalDamage = Rand.Next(min, max);

        // Buff tag increase (Hellspear's impale combo, for ex)
        if (TargetBuffTagId > 0 && target.Buffs.CheckBuffTag(TargetBuffTagId))
        {
            // TODO TargetBuffBonus ? (used in 3 DamageEffects)
            finalDamage *= TargetBuffBonusMul;
        }

        // Toughness reduction (PVP Only)
        if (caster is Character && trg is Character)
            finalDamage *= 1 - trg.BattleResist / (8000f + trg.BattleResist);

        // Do Critical Dmgs
        switch (hitType)
        {
            case SkillHitType.MeleeCritical:
                finalDamage *= 1 + (((Unit)caster).MeleeCriticalBonus - trg.Flexibility / 100) / 100;
                break;
            case SkillHitType.RangedCritical:
                finalDamage *= 1 + (((Unit)caster).RangedCriticalBonus - trg.Flexibility / 100) / 100;
                break;
            case SkillHitType.SpellCritical:
                finalDamage *= 1 + (((Unit)caster).SpellCriticalBonus - trg.Flexibility / 100) / 100;
                break;
            default:
                break;
        }

        // Reduction
        var reductionMul = 1.0f;

        if (target is Unit targetUnit)
        {
            float armor;
            switch (DamageType)
            {
                case DamageType.Melee:
                    armor = Math.Max(0f, targetUnit.Armor - ((Unit)caster).DefensePenetration);
                    reductionMul = 1.0f - armor / (armor + 5300.0f);
                    finalDamage = finalDamage * targetUnit.IncomingMeleeDamageMul;
                    break;
                case DamageType.Ranged:
                    armor = Math.Max(0f, targetUnit.Armor - ((Unit)caster).DefensePenetration);
                    reductionMul = 1.0f - armor / (armor + 5300.0f);
                    finalDamage = finalDamage * targetUnit.IncomingRangedDamageMul;
                    break;
                case DamageType.Magic:
                    armor = Math.Max(0f, targetUnit.MagicResistance - ((Unit)caster).MagicPenetration);
                    reductionMul = 1.0f - armor / (armor + 5300.0f);
                    finalDamage = finalDamage * targetUnit.IncomingSpellDamageMul;
                    break;
                default:
                    finalDamage = finalDamage * targetUnit.IncomingDamageMul;
                    break;
            }
        }
        var value = (int)(finalDamage * reductionMul);
        var absorbed = (int)(finalDamage * (1.0f - reductionMul));
        var healthStolen = (int)(value * (HealthStealRatio / 100.0f));
        var manaStolen = (int)(value * (ManaStealRatio / 100.0f));

        // ID=6151, Test Drive Restriction, 8m
        if (castObj is CastBuff buff && buff.Buff.Template.Id == 6151)
        {
            // TODO I don’t know how to correctly check for the destruction buff of a test car
            // skip the check CanAttack()
        }
        // Safeguard to prevent accidental flagging
        else if (!caster.CanAttack(trg))
            return;

        trg.ReduceCurrentHp(caster, value);
        ((Unit)caster).SummarizeDamage += value;

        if (healthStolen > 0 || manaStolen > 0)
        {
            ((Unit)caster).Hp = Math.Min(((Unit)caster).MaxHp, ((Unit)caster).Hp + healthStolen);
            ((Unit)caster).Mp = Math.Min(((Unit)caster).MaxMp, ((Unit)caster).Mp + manaStolen);
            caster.BroadcastPacket(new SCUnitPointsPacket(caster.ObjId, ((Unit)caster).Hp, ((Unit)caster).Mp), true);
        }

        if (Bonuses != null)
        {
            ((Unit)caster).Bonuses[uint.MaxValue] = [];
        }

        if (caster.GetRelationStateTo(trg) == RelationState.Friendly)
        {
            if (!trg.Buffs.CheckBuff((uint)BuffConstants.Retribution))
            {
                ((Unit)caster).SetCriminalState(true, trg);
            }
        }

        // TODO : Use proper chance kinds (melee, magic etc.)
        var trgCharacter = trg as Character;
        var attacker = caster as Unit;

        // set for all combatants, for RegenTick
        trg.IsInBattle = trg.Hp > 0;
        trg.LastCombatActivity = DateTime.UtcNow;

        if (trgCharacter != null)
        {
            //trgCharacter.IsInBattle |= trg.Hp > 0;
            //trgCharacter.LastCombatActivity = DateTime.UtcNow;
            if (attacker is Character attackerCharacter)
            {
                trgCharacter.SetHostileActivity(attackerCharacter);
            }
            trgCharacter.Procs?.RollProcsForKind(ProcChanceKind.TakeDamageAny);
        }

        if (attacker != null)
        {
            attacker.IsInBattle |= trg.Hp > 0;
            attacker.LastCombatActivity = DateTime.UtcNow;
            attacker.Procs?.RollProcsForKind(ProcChanceKind.HitAny);
        }

        // TODO: Gotta figure out how to tell if it should be applied on getting hit, or on hitting
        caster.CombatBuffs.TriggerCombatBuffs((Unit)caster, target as Unit, hitType, false);
        target.CombatBuffs.TriggerCombatBuffs((Unit)caster, target as Unit, hitType, false);
        var packet = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, value, absorbed)
        {
            HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
            HitType = hitType
        };

        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            trg.BroadcastPacket(packet, true);

        if (trg is Npc npc)
        {
            trg.SendPacketToPlayers([trg, caster], new SCAiAggroPacket(trg.ObjId, 1, caster.ObjId, ((Unit)caster).SummarizeDamage, 0, 0));
            npc.OnDamageReceived((Unit)caster, value);
        }

        //Invoke even if damage is 0
        ((Unit)caster).Events.OnAttack(this, new OnAttackArgs
        {
            Attacker = (Unit)caster
        });
        trg.Events.OnAttacked(this, new OnAttackedArgs { });

        if (value > 0)
        {
            ((Unit)caster).Events.OnDamage(this, new OnDamageArgs
            {
                Attacker = (Unit)caster,
                Amount = value
            });
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageEtc);
            trg.Events.OnDamaged(this, new OnDamagedArgs
            {
                Attacker = (Unit)caster,
                Amount = value
            });

            switch (DamageType)
            {
                case DamageType.Melee:
                    trg.Events.OnDamagedMelee(this, new OnDamagedArgs()
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    break;
                case DamageType.Ranged:
                    trg.Events.OnDamagedRanged(this, new OnDamagedArgs()
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    break;
                case DamageType.Magic:
                    trg.Events.OnDamagedSpell(this, new OnDamagedArgs()
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    break;
                case DamageType.Siege:
                    trg.Events.OnDamagedSiege(this, new OnDamagedArgs()
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    break;
            }

            trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedEtc);
        }
    }
}

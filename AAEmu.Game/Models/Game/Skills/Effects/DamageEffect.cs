using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Housing;
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

        // Every damage calculation below reads Unit combat attributes and equipment. A periodic
        // buff can outlive a non-Unit or removed source, in which case Buff.Caster is null. Such a
        // tick has no authoritative attacker to attribute damage, procs, aggro, or crime to.
        if (caster is not Unit)
            return;

        // House removal debuff (buff 2250) is self-cast (caster == target == the house). CanAttack
        // rejects self-targets, so route the tick to HousingManager, which scales the damage to the
        // house's own MaxHp and owns the wreck/shell timing.
        if (source.Buff?.Id == (uint)BuffConstants.RemovalDebuff && target is House house)
        {
            // Authored damage is the fallback so the target data governs unless a server opts into
            // percentage scaling. damage_effects 1876 authors fixed 10 per 15s tick.
            var authoredDamage = Math.Max(1, FixedMin);
            HousingManager.Instance.ApplyDemolitionTick(house, caster, authoredDamage);
            return;
        }

        if (Bonuses != null)
        {
            foreach (var bonus in Bonuses)
            {
                caster.AddBonus(uint.MaxValue, new Bonus { Template = bonus, Value = bonus.Value });
            }
        }

        // What caused this hit decides which member of the remove_on grid it raises. Every family has an
        // umbrella *_etc raised for any hit of that side, plus a narrow one for the cause: *_spell_dot and
        // *_etc_dot for a damage-over-time tick (magic or anything else), *_buff_trigger for a hit a buff
        // trigger applied rather than a cast. See BuffRemoveOnRules for the row counts behind that.
        var hitCause = BuffRemoveOnRules.HitCause(source?.FromBuffTrigger == true,
            source?.Buff?.TickEffects.Count > 0, DamageType == DamageType.Magic);

        foreach (var flag in BuffRemoveOnRules.AttackedFlags(hitCause))
            trg.Buffs.TriggerRemoveOn(flag);
        foreach (var flag in BuffRemoveOnRules.AttackFlags(hitCause))
            caster.Buffs.TriggerRemoveOn(flag);

        // remove_on_autoattack (146 buffs): the poses a basic attack interrupts — the bard songs 656-667,
        // 연주/율동 performance, 은신, 질주. A weapon auto-attack is the skill itself (2 근접 공격, 3 Offhand,
        // 4 원거리 공격), the same test Skill.cs and CSStartSkillPacket already use; the
        // weapon_slot_for_autoattack_id column is not that marker — see IsAutoAttack's remarks.
        if (BuffRemoveOnRules.IsAutoAttack(source?.Skill?.Template?.Id ?? 0))
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.AutoAttack);

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
            trg.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false, source?.Skill);
            caster.CombatBuffs.TriggerCombatBuffs(caster, trg, hitType, false, source?.Skill);
            caster.BroadcastPacket(missPacket, true);
            return;
        }

        float flexibilityRateMod = trg.Flexibility / 1000 * 3;
        switch (DamageType)
        {
            case DamageType.Melee:
                if (Random.Shared.Next(0f, 100f) < ((Unit)caster).MeleeCritical - flexibilityRateMod)
                    hitType = SkillHitType.MeleeCritical;
                else
                    hitType = SkillHitType.MeleeHit;
                break;
            case DamageType.Magic:
                if (Random.Shared.Next(0f, 100f) < ((Unit)caster).SpellCritical - flexibilityRateMod)
                    hitType = SkillHitType.SpellCritical;
                else
                    hitType = SkillHitType.SpellHit;
                break;
            case DamageType.Ranged:
                if (Random.Shared.Next(0f, 100f) < ((Unit)caster).RangedCritical - flexibilityRateMod)
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
            case DamageType.Siege:
                // siege_dps (260), the siege counterpart of spell_dps: the caster's own contribution to the
                // hit, added to the level damage the same way. No such row means dpsInc stays 0.
                dpsInc = ((Unit)caster).SiegeDps;
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
            // siege_damage_mul (261), the siege counterpart of the three above. Without such a row the
            // factor is exactly 1.0f, so this branch keeps the plain "no type multiplier" it had.
            DamageType.Siege => ((Unit)caster).SiegeDamageMul,
            _ => 1f
        };

        min = MathF.Floor(min * damageMultiplier);
        max = MathF.Ceiling(max * damageMultiplier);

        // Output multiplier against this kind of victim (unit_modifiers 196-198 / 244-246), selected by
        // the target's kind and the same DamageType as the switch above. Applied to the composed result
        // so the Floor/Ceiling above is untouched; an attacker carrying no such bonus gets exactly 1.0f
        // and the arithmetic below is bit-for-bit what it was.
        var antiKindMultiplier = DamageMultiplierRules.SelectDamageMultiplier(
            DamageMultiplierRules.ClassifyVictim(trg),
            DamageType,
            AntiKindDamageMultipliers.From((Unit)caster));

        min *= antiKindMultiplier;
        max *= antiKindMultiplier;

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
            var charged = ChargedBuffRules.CasterBranch(ChargedBuffId, ChargedMul, ChargedLevelMul, source.Skill.Level);
            var effect = caster.Buffs.GetEffectFromBuffId(charged.BuffId);
            var chargeBonus = (effect?.Charge ?? 0) * charged.PerChargeMultiplier;

            min += chargeBonus;
            max += chargeBonus;
            effect?.Exit();
        }

        // No skill guard here: only the caster branch reads source.Skill (its level). This branch is also
        // reached through buff_triggers (damage_effects 4249, 5340, 7140), which carry no skill, so gating
        // it on one silently dropped the target's charge bonus.
        if (UseTargetChargedBuff)
        {
            var charged = ChargedBuffRules.TargetBranch(TargetChargedBuffId, TargetChargedMul);
            var effect = target.Buffs.GetEffectFromBuffId(charged.BuffId);
            var chargeBonus = (effect?.Charge ?? 0) * charged.PerChargeMultiplier;

            min += chargeBonus;
            max += chargeBonus;
            effect?.Exit();
        }

        if (UseFixedDamage)
        {
            min = FixedMin;
            max = FixedMax;
        }

        var finalDamage = Random.Shared.Next(min, max);

        // AoE diminishing: the same area skill's successive hits on one unit take the next aoe_diminishings
        // rate. Off unless the content says otherwise — an empty table, a single-target skill, a skill whose
        // plot event is not flagged and a buff tick all leave the factor at exactly 1.0f, so the arithmetic
        // below is bit-for-bit what it was.
        var aoeDiminishing = AoeDiminishingMultiplier(source, castObj, trg, time);
        finalDamage *= aoeDiminishing;

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
                case DamageType.Siege:
                    // incoming_siege_damage_mul (149), read off the victim. Siege damage still takes no
                    // armour reduction, as it did on the default branch, and still takes IncomingDamageMul:
                    // a victim without a 149 row has a factor of exactly 1.0f here and keeps its numbers.
                    finalDamage = finalDamage * targetUnit.IncomingSiegeDamageMul * targetUnit.IncomingDamageMul;
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

        // ID=6151 Test Drive, or explicitly authorized plot self-damage, may bypass CanAttack.
        if (!caster.CanAttack(trg) && !AllowsCanAttackBypass(castObj, caster, trg))
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

        var trgCharacter = trg as Character;
        var attacker = caster as Unit;
        
        if (CheckCrime && caster.GetRelationStateTo(trg) == RelationState.Friendly)
        {
            // Set Purple state
            if (!trg.Buffs.CheckBuff((uint)BuffConstants.Retribution))
            {
                ((Unit)caster).SetCriminalState(true, trg);
            }

            // Mark the owner of this unit as being assaulted
            var targetOwner = trg.GetOwnerCharacter();
            var sourceOwner = caster.GetOwnerCharacter();
            if (targetOwner != null && sourceOwner != null)
            {
                // If both players haven't interacted with each other yet, then generate evidence for the initiator 
                if ((!sourceOwner.AssaultedBy.Contains(targetOwner.Id)) && (!targetOwner.AssaultedBy.Contains(sourceOwner.Id)))
                {
                    // Update assault list
                    targetOwner.AssaultedBy.Add(sourceOwner.Id);
                    sourceOwner.AssaultOn.Add(targetOwner.Id);
                    // Generate evidence
                    _ = CrimeManager.Instance.GenerateEvidenceFromDamage(caster, trg);
                }
            }
        }

        // TODO : Use proper chance kinds (melee, magic etc.)

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
        caster.CombatBuffs.TriggerCombatBuffs((Unit)caster, target as Unit, hitType, false, source?.Skill);
        target.CombatBuffs.TriggerCombatBuffs((Unit)caster, target as Unit, hitType, false, source?.Skill);
        var packet = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, value, absorbed)
        {
            HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
            HitType = hitType
        };

        if (packetBuilder != null)
            packetBuilder.AddPacket(packet);
        else
            trg.BroadcastPacket(packet, true);

        if (WorldIntegration.ZoneAuthority && packetBuilder == null)
        {
            // Zone WZUnitDamaged applies HP + aggro. Also sending WZUnitPoints (absolute HP) on the
            // same hit double-applies damage on Zone → HP desync → mid-fight leash/11503 reset.
            uint wzSkillId = 0;
            ushort wzTl = 0;
            var sendDamaged = Environment.GetEnvironmentVariable("AAEMU_WZ_UNIT_DAMAGED") != "0" &&
                              TryGetSkillCastIds(castObj, out wzSkillId, out wzTl);
            if (sendDamaged)
            {
                WorldIntegration.RelayUnitDamagedToZone?.Invoke(
                    wzSkillId, wzTl, casterObj, targetObj, value, absorbed, caster.ObjId, trg.ObjId);
            }
            else
            {
                ZoneAuthorityCombat.SyncUnitPoints(trg.ObjId, trg.Hp, trg.Mp);
            }
        }

        if (trg is Npc npc)
        {
            if (!WorldIntegration.ZoneAuthority)
            {
                trg.SendPacketToPlayers(
                    [trg, caster],
                    new SCAiAggroPacket(
                        trg.ObjId,
                        AiAggroEntry.FromDamageValue(caster.ObjId, ((Unit)caster).SummarizeDamage)));
            }

            npc.OnDamageReceived((Unit)caster, value);
        }

        //Invoke even if damage is 0
        ((Unit)caster).Events.OnAttack(this, new OnAttackArgs
        {
            Attacker = (Unit)caster,
            Target = trg
        });
        trg.Events.OnAttacked(this, new OnAttackedArgs { Attacker = (Unit)caster });

        if (value > 0)
        {
            var damageArgs = new OnDamageArgs
            {
                Attacker = (Unit)caster,
                Amount = value,
                Target = trg
            };
            ((Unit)caster).Events.OnDamage(this, damageArgs);
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageEtc);
            // The narrow flag beside the umbrella for this cause. A tick's DamageSpellDot/DamageEtcDot is
            // raised where the tick is recognised, further up; a trigger's DamageBuffTrigger belongs here,
            // with the damage that was actually dealt.
            if (hitCause == BuffHitCause.BuffTrigger)
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamageBuffTrigger);
            trg.Events.OnDamaged(this, new OnDamagedArgs
            {
                Attacker = (Unit)caster,
                Amount = value
            });

            switch (DamageType)
            {
                case DamageType.Melee:
                    trg.Events.OnDamagedMelee(this, new OnDamagedArgs
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    // The attacker's own side of the same hit, split by the type that caused it.
                    ((Unit)caster).Events.OnDamageMelee(this, damageArgs);
                    break;
                case DamageType.Ranged:
                    trg.Events.OnDamagedRanged(this, new OnDamagedArgs
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    ((Unit)caster).Events.OnDamageRanged(this, damageArgs);
                    break;
                case DamageType.Magic:
                    trg.Events.OnDamagedSpell(this, new OnDamagedArgs
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    ((Unit)caster).Events.OnDamageSpell(this, damageArgs);
                    break;
                case DamageType.Siege:
                    trg.Events.OnDamagedSiege(this, new OnDamagedArgs
                    {
                        Attacker = (Unit)caster,
                        Amount = value
                    });
                    ((Unit)caster).Events.OnDamageSiege(this, damageArgs);
                    break;
            }

            trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedEtc);
            if (hitCause == BuffHitCause.BuffTrigger)
                trg.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.DamagedBuffTrigger);
        }
    }

    /// <summary>
    /// The factor AoE diminishing applies to this hit: exactly 1.0f whenever the content does not call for it.
    /// </summary>
    /// <remarks>
    /// A hit diminishes when the table has rows, the hit came from a cast rather than a buff tick, and either
    /// the casting skill's plot event carries <c>aoe_diminishing</c> or the skill is itself an area skill whose
    /// plot does. The counter is per (unit, skill) and advances only on a hit that actually diminishes, so a
    /// skill that is exempt does not spend the next rate for the skill that is not.
    /// </remarks>
    private static float AoeDiminishingMultiplier(EffectSource source, CastAction castObj, Unit trg, DateTime time)
    {
        if (!AoeDiminishingTable.IsLoaded)
            return 1.0f;

        // A tick of a damage-over-time is the same application landing again, not a new area hit.
        if (source?.Buff?.TickEffects.Count > 0)
            return 1.0f;

        var skillId = source?.Skill?.Template?.Id ?? 0;
        var plotCast = castObj as CastPlot;
        if (skillId == 0 && plotCast == null)
            return 1.0f;

        var isAreaSkill = source?.Skill?.Template is { } skillTemplate &&
                          AoeDiminishingRules.IsAreaSkill(skillTemplate.TargetAreaCount, skillTemplate.TargetAreaRadius);

        var plotFlagged = false;
        var plotId = source?.Skill?.Template?.Plot?.Id ?? 0;
        if (plotId != 0)
            plotFlagged = source.Skill.Template.Plot.EventTemplate?.AoeDiminishing == true;

        var plotCastFlagged = plotCast != null &&
                              PlotManager.Instance?.GetEventByPlotId(plotCast.PlotId)?.AoeDiminishing == true;

        if (!AoeDiminishingRules.Diminishes(isAreaSkill, plotFlagged, plotCastFlagged))
            return 1.0f;

        var counterSkillId = skillId != 0 ? skillId : plotCast?.PlotId ?? 0;
        var rate = AoeDiminishingTracker.Advance(trg.ObjId, counterSkillId, time, AoeDiminishingTable.Rates);
        return AoeDiminishingRules.RateMultiplier(rate);
    }

    private static bool TryGetSkillCastIds(CastAction castObj, out uint skillId, out ushort tlId)
    {
        switch (castObj)
        {
            case CastSkill cs:
                skillId = cs.SkillId;
                tlId = cs.TlId;
                return true;
            case CastPlot cp:
                skillId = cp.SkillId;
                tlId = cp.TlId;
                return skillId != 0;
            default:
                skillId = 0;
                tlId = 0;
                return false;
        }
    }

    /// <summary>
    /// When <see cref="BaseUnit.CanAttack"/> is false, only these cast contexts may still apply damage.
    /// </summary>
    internal static bool AllowsCanAttackBypass(CastAction castObj, BaseUnit caster, BaseUnit trg)
    {
        if (castObj is CastBuff buff && buff.Buff?.Template?.Id == 6151)
            return true;

        // Plot self-hit is not globally trusted. World arms an explicit predicate for the
        // intended flow (e.g. tower-def kill-quota restore devices).
        if (castObj is not CastPlot)
            return false;
        if (caster == null || trg == null || caster.ObjId != trg.ObjId)
            return false;
        return WorldIntegration.AllowsPlotSelfDamageBypass?.Invoke(trg) == true;
    }
}

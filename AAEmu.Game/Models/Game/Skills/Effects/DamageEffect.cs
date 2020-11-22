using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
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

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("DamageEffect");

            if (!(target is Unit))
            {
                return;
            }

            //Safeguard for avoiding false positive flagging
            if (!caster.CanAttack(target))
                return;

            var weapon = caster?.Equipment.GetItemBySlot(WeaponSlotId);
            var holdable = (WeaponTemplate)weapon?.Template;

            var trg = (Unit)target;

            var hitType = SkillHitType.Invalid;
            if ((source?.Skill?.HitTypes.TryGetValue(trg.ObjId, out hitType) ?? false )
                && (source?.Skill.SkillMissed(trg.ObjId) ?? false))
            {
                var missPacket = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, 0, 0)
                {
                    HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
                    HitType = hitType
                };
                caster.BroadcastPacket(missPacket, true);
                return;
            }

            float flexibilityRateMod = (trg.Flexibility / 1000 * 3);
            switch (DamageType)
            {
                case DamageType.Melee:
                    if (Rand.Next(0f, 100f) < (caster.MeleeCritical - flexibilityRateMod))
                        hitType = SkillHitType.MeleeCritical;
                    else
                        hitType = SkillHitType.MeleeHit;
                    break;
                case DamageType.Magic:
                    if (Rand.Next(0f, 100f) < (caster.SpellCritical - flexibilityRateMod))
                        hitType = SkillHitType.SpellCritical;
                    else
                        hitType = SkillHitType.SpellHit;
                    break;
                case DamageType.Ranged:
                    if (Rand.Next(0f, 100f) < (caster.RangedCritical - flexibilityRateMod))
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
            
            if (UseFixedDamage)
            {
                min += FixedMin;
                max += FixedMax;
            }

            // Used for NPCs, I think
            var levelMin = 0.0f;
            var levelMax = 0.0f;
            if (UseLevelDamage) 
            {
                var lvlMd = caster.LevelDps * LevelMd;
                // Hack null-check on skill
                var levelModifier = (( (source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;
            
                levelMin += (lvlMd - levelModifier * lvlMd) + 0.5f;
                levelMax += (levelModifier + 1) * lvlMd + 0.5f;
            }

            
            if (UseChargedBuff && source.Skill != null)
            {
                var charges = caster.Effects.GetEffectFromBuffId(ChargedBuffId)?.Charge ?? 0;
                
                min += charges * ChargedMul * (source.Skill.Level * ChargedLevelMul);
                max += charges * ChargedMul * (source.Skill.Level * ChargedLevelMul);
            }

            if (UseTargetChargedBuff && source.Skill != null)
            {
                var charges = target.Effects.GetEffectFromBuffId(ChargedBuffId).Charge;
                
                min += charges * ChargedMul * (source.Skill.Level * ChargedLevelMul);
                max += charges * ChargedMul * (source.Skill.Level * ChargedLevelMul);
            }
            
            // Stats/Weapon DPS
            var dpsInc = 0;
            switch (DamageType)
            {
                case DamageType.Melee:
                    dpsInc = caster.DpsInc;
                    break;
                case DamageType.Magic:
                    dpsInc = caster.MDps + caster.MDpsInc;
                    break;
                case DamageType.Ranged:
                    dpsInc = caster.RangedDpsInc;
                    break;
            }

            max += (dpsInc * 0.001f) * DpsIncMultiplier;
            var weaponDamage = 0.0f;

            if (UseMainhandWeapon)
                weaponDamage = caster.Dps * 0.001f; // TODO : Use only weapon value!
            if (UseOffhandWeapon)
                weaponDamage = (caster.OffhandDps * 0.001f) + weaponDamage;
            if (UseRangedWeapon)
                weaponDamage = (caster.RangedDps * 0.001f) + weaponDamage; // TODO : Use only weapon value!

            max = (DpsMultiplier * weaponDamage) + max;
            
            var minCastBonus = 1000f;
            // Hack null-check on skill
            var castTimeMod = source.Skill?.Template.CastingTime ?? 0 ; // This mod depends on casting_inc too!
            if (castTimeMod <= 1000)
                minCastBonus = min > 0 ? min : minCastBonus;
            else
                minCastBonus = castTimeMod;

            var variableDamage = (max * minCastBonus * 0.001f);
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

            var damageMultiplier = 0.0f;
            switch (DamageType)
            {
                case DamageType.Melee:
                    // damageMultiplier = caster.Dps???
                    damageMultiplier = 1.0f;
                    break;
                case DamageType.Magic:
                    damageMultiplier = 1.0f;
                    break;
                case DamageType.Ranged:
                    damageMultiplier = 1.0f;
                    break;
                case DamageType.Siege:
                    // TODO 
                    damageMultiplier = 1.0f;
                    break;
            }

            var iVar1 = (int)(min * (damageMultiplier + 1000));
            var uVar3 = iVar1 / 1000 + (iVar1 >> 0x1f);
            min = (uVar3 >> 0x1f) + uVar3;
            iVar1 = (int)(max * (damageMultiplier + 1000));
            uVar3 = iVar1 / 1000 + (iVar1 >> 0x1f);
            max = (uVar3 >> 0x1f) + uVar3;

            if (source.Skill != null)
            {
                min = (float)caster.Modifiers.ApplyModifiers(source.Skill, SkillAttribute.Damage, min);
                max = (float)caster.Modifiers.ApplyModifiers(source.Skill, SkillAttribute.Damage, max);
            }

            if (source.Buff?.TickEffect != null)
            {
                min = (float) (min * (source.Buff.Tick / source.Buff.Duration));
                max = (float) (max * (source.Buff.Tick / source.Buff.Duration));
            }

            var finalDamage = Rand.Next(min, max);

            //toughness reduction (PVP Only)
            if (caster is Character && trg is Character)
                finalDamage *= ( 1 - ( trg.BattleResist / ( 8000f + trg.BattleResist ) ) );

            //Do Critical Dmgs
            switch (hitType)
            {
                case SkillHitType.MeleeCritical:
                    finalDamage *= 1 + ((caster.MeleeCriticalBonus - (trg.Flexibility / 100)) / 100);
                    break;
                case SkillHitType.RangedCritical:
                    finalDamage *= 1 + ((caster.RangedCriticalBonus - (trg.Flexibility / 100)) / 100);
                    break;
                case SkillHitType.SpellCritical:
                    finalDamage *= 1 + ((caster.SpellCriticalBonus - (trg.Flexibility / 100)) / 100);
                    break;
                default:
                    break;
            }

            // Reduction
            var reductionMul = 1.0f;

            if (target is Unit targetUnit)
            {
                switch (DamageType)
                {
                    case DamageType.Melee:
                    case DamageType.Ranged:
                        reductionMul = 1.0f - targetUnit.Armor / (targetUnit.Armor + 5300.0f);
                        break;
                    case DamageType.Magic:
                        reductionMul = 1.0f - (targetUnit.MagicResistance) / (targetUnit.MagicResistance + 5300.0f);
                        break;
                }
            }
            var value = (int)(finalDamage * reductionMul);
            var absorbed = (int)(finalDamage * (1.0f - reductionMul));
            trg.ReduceCurrentHp(caster, value);
            caster.SummarizeDamage += value;

            if (caster.GetRelationStateTo(trg) == RelationState.Friendly)
            {
                if (!trg.Effects.CheckBuff((uint)BuffConstants.RETRIBUTION_BUFF))
                {
                    caster.SetCriminalState(true);
                }
            }

            // TODO : Use proper chance kinds (melee, magic etc.)
            if (trg is Character procTarget)
                procTarget.Procs.RollProcsForKind(ProcChanceKind.TakeDamageAny);
            if (caster is Character procAttacker)
                procAttacker.Procs.RollProcsForKind(ProcChanceKind.HitAny);

            var packet = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, value, absorbed)
            {
                HoldableId = (byte)(holdable?.HoldableTemplate?.Id ?? 0),
                HitType = hitType
            };
            
            if (packetBuilder != null) 
                packetBuilder.AddPacket(packet);
            else
                trg.BroadcastPacket(packet, true);
            if (trg is Npc)
            {
                trg.BroadcastPacket(new SCAiAggroPacket(trg.ObjId, 1, caster.ObjId, caster.SummarizeDamage), true);
            }
            if (trg is Npc npc && npc.CurrentTarget != caster)
            {
                npc.OnDamageReceived(caster);
            }

            //Invoke even if damage is 0
            caster.Events.OnAttack(this, new OnAttackArgs
            {
                Attacker = caster
            });
            trg.Events.OnAttacked(this, new OnAttackedArgs { });

            if (value > 0)
            {
                caster.Events.OnDamage(this, new OnDamageArgs {
                    Attacker = caster
                    
                });
                trg.Events.OnDamaged(this, new OnDamagedArgs { });
            }
        }
    }
}

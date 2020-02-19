using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Tasks.UnitMove;

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
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time)
        {
            _log.Debug("DamageEffect");

            if (!(target is Unit))
            {
                return;
            }

            var trg = (Unit)target;
            var min = 0;
            var max = 0;

            if (UseFixedDamage)
            {
                min += FixedMin;
                max += FixedMax;
            }

            var unk = 0f;
            var unk2 = 1f;
            var skillLevel = 1;
            if (skill != null)
            {
                skillLevel = (skill.Level - 1) * skill.Template.LevelStep + skill.Template.AbilityLevel;
                if (skillLevel >= skill.Template.AbilityLevel)
                {
                    unk = 0.015f * (skillLevel - skill.Template.AbilityLevel + 1);
                }
                unk2 = (1 + unk) * 1.3f;
            }

            if (UseLevelDamage)
            {
                var levelMd = (unk + 1) * LevelMd;
                min += (int)(caster.LevelDps * levelMd + 0.5f);
                max += (int)((((skillLevel - 1) * 0.020408163f * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.0099999998f + 1f) * caster.LevelDps * levelMd + 0.5f);
            }

            var dpsInc = 0f;
            if (DamageType == DamageType.Melee)
            {
                dpsInc = caster.DpsInc;
            }
            else if (DamageType == DamageType.Magic)
            {
                dpsInc = caster.MDps + caster.MDpsInc;
            }
            else if (DamageType == DamageType.Ranged)
            {
                dpsInc = caster.RangedDpsInc;
            }

            var dps = 0f;
            if (UseMainhandWeapon)
            {
                dps += caster.Dps;
            }
            else if (UseOffhandWeapon)
            {
                dps += caster.OffhandDps;
            }
            else if (UseRangedWeapon)
            {
                dps += caster.RangedDps;
            }

            if (dps <= 0) // TODO убрать этот костыль
            {
                dps = 15000f * caster.Level;
            }
            if (dpsInc <= 0)
            {
                dpsInc = 2000f * caster.Level;
            }

            min += (int)((DpsMultiplier * dps * 0.001f + DpsIncMultiplier * dpsInc * 0.001f) * unk2 + 0.5f);
            max += (int)((DpsMultiplier * dps * 0.001f + DpsIncMultiplier * dpsInc * 0.001f) * unk2 + 0.5f);
            min = (int)(min * Multiplier);
            max = (int)(max * Multiplier);
            var value = Rand.Next(min, max);
            trg.ReduceCurrentHp(caster, value);
            caster.SummarizeDamage += value;
            trg.BroadcastPacket(new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, value), true);
            if (trg is Npc)
            {
                trg.BroadcastPacket(new SCAiAggroPacket(trg.ObjId, 1, caster.ObjId, caster.SummarizeDamage), true);
            }
            if (trg is Npc npc && npc.CurrentTarget != caster)
            {
                //npc.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, caster.ObjId), true);

                if (npc.Patrol == null || npc.Patrol.PauseAuto(npc))
                {
                    npc.CurrentTarget = caster;
                    npc.BroadcastPacket(new SCCombatEngagedPacket(caster.ObjId), true); // caster
                    npc.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true);    // target
                    npc.BroadcastPacket(new SCCombatFirstHitPacket(npc.ObjId, caster.ObjId, 0), true);
                    npc.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, caster.ObjId), true);
                    npc.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, caster.ObjId), true);

                    TaskManager.Instance.Schedule(new UnitMove(new Track(), npc), TimeSpan.FromMilliseconds(100));
                }
            }
        }
    }
}

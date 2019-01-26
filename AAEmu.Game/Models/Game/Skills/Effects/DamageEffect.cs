using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

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

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, DateTime time)
        {
            _log.Debug("DamageEffect");
        }
    }
}

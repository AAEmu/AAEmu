using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DamageEffect
{
    public long? Id { get; set; }

    public long? DamageTypeId { get; set; }

    public long? FixedMin { get; set; }

    public long? FixedMax { get; set; }

    public double? Multiplier { get; set; }

    public byte[] UseMainhandWeapon { get; set; }

    public byte[] UseOffhandWeapon { get; set; }

    public byte[] UseRangedWeapon { get; set; }

    public long? CriticalBonus { get; set; }

    public long? TargetBuffTagId { get; set; }

    public long? TargetBuffBonus { get; set; }

    public byte[] UseFixedDamage { get; set; }

    public byte[] UseLevelDamage { get; set; }

    public double? LevelMd { get; set; }

    public long? LevelVaStart { get; set; }

    public long? LevelVaEnd { get; set; }

    public double? TargetBuffBonusMul { get; set; }

    public byte[] UseChargedBuff { get; set; }

    public long? ChargedBuffId { get; set; }

    public double? ChargedMul { get; set; }

    public double? AggroMultiplier { get; set; }

    public long? HealthStealRatio { get; set; }

    public long? ManaStealRatio { get; set; }

    public double? DpsMultiplier { get; set; }

    public long? WeaponSlotId { get; set; }

    public byte[] CheckCrime { get; set; }

    public long? HitAnimTimingId { get; set; }

    public byte[] UseTargetChargedBuff { get; set; }

    public long? TargetChargedBuffId { get; set; }

    public double? TargetChargedMul { get; set; }

    public double? DpsIncMultiplier { get; set; }

    public byte[] EngageCombat { get; set; }

    public byte[] Synergy { get; set; }

    public long? ActabilityGroupId { get; set; }

    public long? ActabilityStep { get; set; }

    public double? ActabilityMul { get; set; }

    public double? ActabilityAdd { get; set; }

    public double? ChargedLevelMul { get; set; }

    public byte[] AdjustDamageByHeight { get; set; }

    public byte[] UsePercentDamage { get; set; }

    public long? PercentMin { get; set; }

    public long? PercentMax { get; set; }

    public byte[] UseCurrentHealth { get; set; }

    public long? TargetHealthMin { get; set; }

    public long? TargetHealthMax { get; set; }

    public double? TargetHealthMul { get; set; }

    public long? TargetHealthAdd { get; set; }

    public byte[] FireProc { get; set; }
}

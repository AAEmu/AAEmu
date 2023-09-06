using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Buff
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Desc { get; set; }

    public long? IconId { get; set; }

    public long? AnimStartId { get; set; }

    public long? AnimEndId { get; set; }

    public long? Duration { get; set; }

    public long? Tick { get; set; }

    public byte[] Silence { get; set; }

    public byte[] Root { get; set; }

    public byte[] Sleep { get; set; }

    public byte[] Stun { get; set; }

    public byte[] Crippled { get; set; }

    public byte[] Stealth { get; set; }

    public byte[] RemoveOnSourceDead { get; set; }

    public long? LinkBuffId { get; set; }

    public long? TickManaCost { get; set; }

    public long? StackRuleId { get; set; }

    public long? InitMinCharge { get; set; }

    public long? InitMaxCharge { get; set; }

    public long? MaxStack { get; set; }

    public long? DamageAbsorptionTypeId { get; set; }

    public long? DamageAbsorptionPerHit { get; set; }

    public long? AuraRadius { get; set; }

    public long? ManaShieldRatio { get; set; }

    public long? FxGroupId { get; set; }

    public byte[] Framehold { get; set; }

    public byte[] Ragdoll { get; set; }

    public byte[] OneTime { get; set; }

    public long? ReflectionChance { get; set; }

    public long? ReflectionTypeId { get; set; }

    public long? RequireBuffId { get; set; }

    public byte[] Taunt { get; set; }

    public byte[] TauntWithTopAggro { get; set; }

    public byte[] RemoveOnUseSkill { get; set; }

    public byte[] MeleeImmune { get; set; }

    public byte[] SpellImmune { get; set; }

    public byte[] RangedImmune { get; set; }

    public byte[] SiegeImmune { get; set; }

    public long? ImmuneDamage { get; set; }

    public long? SkillControllerId { get; set; }

    public long? ResurrectionHealth { get; set; }

    public long? ResurrectionMana { get; set; }

    public byte[] ResurrectionPercent { get; set; }

    public long? LevelDuration { get; set; }

    public long? ReflectionRatio { get; set; }

    public long? ReflectionTargetRatio { get; set; }

    public byte[] KnockbackImmune { get; set; }

    public long? ImmuneBuffTagId { get; set; }

    public long? AuraRelationId { get; set; }

    public long? GroupId { get; set; }

    public long? GroupRank { get; set; }

    public byte[] PerUnitCreation { get; set; }

    public double? TickAreaRadius { get; set; }

    public long? TickAreaRelationId { get; set; }

    public byte[] RemoveOnMove { get; set; }

    public byte[] UseSourceFaction { get; set; }

    public long? FactionId { get; set; }

    public byte[] Exempt { get; set; }

    public long? TickAreaFrontAngle { get; set; }

    public long? TickAreaAngle { get; set; }

    public byte[] Psychokinesis { get; set; }

    public byte[] NoCollide { get; set; }

    public double? PsychokinesisSpeed { get; set; }

    public byte[] RemoveOnDeath { get; set; }

    public byte[] CombatTextStart { get; set; }

    public byte[] CombatTextEnd { get; set; }

    public long? TickAnimId { get; set; }

    public long? TickActiveWeaponId { get; set; }

    public byte[] ConditionalTick { get; set; }

    public byte[] System { get; set; }

    public long? AuraSlaveBuffId { get; set; }

    public byte[] NonPushable { get; set; }

    public long? ActiveWeaponId { get; set; }

    public long? CustomDualMaterialId { get; set; }

    public double? CustomDualMaterialFadeTime { get; set; }

    public long? MaxCharge { get; set; }

    public byte[] DetectStealth { get; set; }

    public byte[] RemoveOnExempt { get; set; }

    public byte[] RemoveOnLand { get; set; }

    public byte[] Gliding { get; set; }

    public long? GlidingRotateSpeed { get; set; }

    public byte[] KnockDown { get; set; }

    public byte[] TickAreaExcludeSource { get; set; }

    public long? StringInstrumentStartAnimId { get; set; }

    public long? PercussionInstrumentStartAnimId { get; set; }

    public long? TubeInstrumentStartAnimId { get; set; }

    public long? StringInstrumentTickAnimId { get; set; }

    public long? PercussionInstrumentTickAnimId { get; set; }

    public long? TubeInstrumentTickAnimId { get; set; }

    public double? GlidingStartupTime { get; set; }

    public double? GlidingStartupSpeed { get; set; }

    public double? GlidingFallSpeedSlow { get; set; }

    public double? GlidingFallSpeedNormal { get; set; }

    public double? GlidingFallSpeedFast { get; set; }

    public double? GlidingSmoothTime { get; set; }

    public long? GlidingLiftCount { get; set; }

    public double? GlidingLiftHeight { get; set; }

    public double? GlidingLiftValidTime { get; set; }

    public double? GlidingLiftDuration { get; set; }

    public double? GlidingLiftSpeed { get; set; }

    public double? GlidingLandHeight { get; set; }

    public double? GlidingSlidingTime { get; set; }

    public double? GlidingMoveSpeedSlow { get; set; }

    public double? GlidingMoveSpeedNormal { get; set; }

    public double? GlidingMoveSpeedFast { get; set; }

    public byte[] FallDamageImmune { get; set; }

    public long? KindId { get; set; }

    public string AgStance { get; set; }

    public long? TransformBuffId { get; set; }

    public byte[] BlankMinded { get; set; }

    public byte[] Fastened { get; set; }

    public byte[] SlaveApplicable { get; set; }

    public byte[] Pacifist { get; set; }

    public byte[] RemoveOnInteraction { get; set; }

    public byte[] Crime { get; set; }

    public byte[] RemoveOnUnmount { get; set; }

    public byte[] AuraChildOnly { get; set; }

    public byte[] RemoveOnMount { get; set; }

    public byte[] RemoveOnStartSkill { get; set; }

    public byte[] SprintMotion { get; set; }

    public double? TelescopeRange { get; set; }

    public long? MainhandToolId { get; set; }

    public long? OffhandToolId { get; set; }

    public long? TickMainhandToolId { get; set; }

    public long? TickOffhandToolId { get; set; }

    public double? TickLevelManaCost { get; set; }

    public byte[] WalkOnly { get; set; }

    public byte[] CannotJump { get; set; }

    public long? CrowdBuffId { get; set; }

    public double? CrowdRadius { get; set; }

    public long? CrowdNumber { get; set; }

    public byte[] EvadeTelescope { get; set; }

    public double? TransferTelescopeRange { get; set; }

    public byte[] RemoveOnAttackSpellDot { get; set; }

    public byte[] RemoveOnAttackEtcDot { get; set; }

    public byte[] RemoveOnAttackBuffTrigger { get; set; }

    public byte[] RemoveOnAttackEtc { get; set; }

    public byte[] RemoveOnAttackedSpellDot { get; set; }

    public byte[] RemoveOnAttackedEtcDot { get; set; }

    public byte[] RemoveOnAttackedBuffTrigger { get; set; }

    public byte[] RemoveOnAttackedEtc { get; set; }

    public byte[] RemoveOnDamageSpellDot { get; set; }

    public byte[] RemoveOnDamageEtcDot { get; set; }

    public byte[] RemoveOnDamageBuffTrigger { get; set; }

    public byte[] RemoveOnDamageEtc { get; set; }

    public byte[] RemoveOnDamagedSpellDot { get; set; }

    public byte[] RemoveOnDamagedEtcDot { get; set; }

    public byte[] RemoveOnDamagedBuffTrigger { get; set; }

    public byte[] RemoveOnDamagedEtc { get; set; }

    public byte[] OwnerOnly { get; set; }

    public byte[] RemoveOnAutoattack { get; set; }

    public long? SaveRuleId { get; set; }

    public string IdleAnim { get; set; }

    public byte[] AntiStealth { get; set; }

    public double? Scale { get; set; }

    public double? ScaleDuration { get; set; }

    public byte[] ImmuneExceptCreator { get; set; }

    public long? ImmuneExceptSkillTagId { get; set; }

    public double? FindSchoolOfFishRange { get; set; }

    public long? AnimActionId { get; set; }

    public byte[] DeadApplicable { get; set; }

    public byte[] TickAreaUseOriginSource { get; set; }

    public byte[] RealTime { get; set; }

    public byte[] DoNotRemoveByOtherSkillController { get; set; }

    public long? CooldownSkillId { get; set; }

    public long? CooldownSkillTime { get; set; }

    public byte[] ManaBurnImmune { get; set; }

    public byte[] FreezeShip { get; set; }

    public byte[] NoCollideRigid { get; set; }

    public byte[] CrowdFriendly { get; set; }

    public byte[] CrowdHostile { get; set; }

    public byte[] NameTr { get; set; }

    public byte[] DescTr { get; set; }

    public byte[] NoExpPenalty { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Skill
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Desc { get; set; }

    public long? Cost { get; set; }

    public long? IconId { get; set; }

    public byte[] Show { get; set; }

    public long? StartAnimId { get; set; }

    public long? FireAnimId { get; set; }

    public long? AbilityId { get; set; }

    public long? ManaCost { get; set; }

    public long? TimingId { get; set; }

    public long? WeaponSlotForAutoattackId { get; set; }

    public long? CooldownTime { get; set; }

    public long? CastingTime { get; set; }

    public byte[] IgnoreGlobalCooldown { get; set; }

    public long? EffectDelay { get; set; }

    public double? EffectSpeed { get; set; }

    public long? EffectRepeatCount { get; set; }

    public long? EffectRepeatTick { get; set; }

    public long? CategoryId { get; set; }

    public long? ActiveWeaponId { get; set; }

    public long? TargetTypeId { get; set; }

    public long? TargetSelectionId { get; set; }

    public long? TargetRelationId { get; set; }

    public long? TargetAreaCount { get; set; }

    public long? TargetAreaRadius { get; set; }

    public byte[] TargetSiege { get; set; }

    public long? WeaponSlotForAngleId { get; set; }

    public long? TargetAngle { get; set; }

    public long? WeaponSlotForRangeId { get; set; }

    public long? MinRange { get; set; }

    public long? MaxRange { get; set; }

    public byte[] KeepStealth { get; set; }

    public byte[] StopAutoattack { get; set; }

    public long? Aggro { get; set; }

    public long? FxGroupId { get; set; }

    public long? ProjectileId { get; set; }

    public byte[] CheckObstacle { get; set; }

    public long? ChannelingTime { get; set; }

    public long? ChannelingTick { get; set; }

    public long? ChannelingMana { get; set; }

    public long? ChannelingAnimId { get; set; }

    public long? ChannelingTargetBuffId { get; set; }

    public long? TargetAreaAngle { get; set; }

    public long? AbilityLevel { get; set; }

    public long? ChannelingDoodadId { get; set; }

    public long? CooldownTagId { get; set; }

    public long? SkillControllerId { get; set; }

    public long? RepeatCount { get; set; }

    public long? RepeatTick { get; set; }

    public long? ToggleBuffId { get; set; }

    public byte[] TargetDead { get; set; }

    public long? ChannelingBuffId { get; set; }

    public long? ReagentCorpseStatusId { get; set; }

    public byte[] SourceDead { get; set; }

    public long? LevelStep { get; set; }

    public double? ValidHeight { get; set; }

    public double? TargetValidHeight { get; set; }

    public byte[] SourceMount { get; set; }

    public byte[] StopCastingOnBigHit { get; set; }

    public byte[] StopChannelingOnBigHit { get; set; }

    public byte[] AutoLearn { get; set; }

    public byte[] NeedLearn { get; set; }

    public long? MainhandToolId { get; set; }

    public long? OffhandToolId { get; set; }

    public long? FrontAngle { get; set; }

    public double? ManaLevelMd { get; set; }

    public long? TwohandFireAnimId { get; set; }

    public byte[] Unmount { get; set; }

    public long? DamageTypeId { get; set; }

    public byte[] AllowToPrisoner { get; set; }

    public long? MilestoneId { get; set; }

    public byte[] MatchAnimation { get; set; }

    public long? PlotId { get; set; }

    public byte[] UseAnimTime { get; set; }

    public byte[] StartAutoattack { get; set; }

    public long? ConsumeLp { get; set; }

    public byte[] SourceStun { get; set; }

    public byte[] TargetAlive { get; set; }

    public string WebDesc { get; set; }

    public byte[] TargetWater { get; set; }

    public byte[] UseSkillCamera { get; set; }

    public byte[] ControllerCamera { get; set; }

    public double? CameraSpeed { get; set; }

    public long? ControllerCameraSpeed { get; set; }

    public double? CameraMaxDistance { get; set; }

    public double? CameraDuration { get; set; }

    public double? CameraAcceleration { get; set; }

    public double? CameraSlowDownDistance { get; set; }

    public byte[] CameraHoldZ { get; set; }

    public long? CastingInc { get; set; }

    public byte[] CastingCancelable { get; set; }

    public byte[] CastingDelayable { get; set; }

    public byte[] ChannelingCancelable { get; set; }

    public double? TargetOffsetAngle { get; set; }

    public double? TargetOffsetDistance { get; set; }

    public long? ActabilityGroupId { get; set; }

    public byte[] PlotOnly { get; set; }

    public double? PitchAngle { get; set; }

    public byte[] SkillControllerAtEnd { get; set; }

    public byte[] EndSkillController { get; set; }

    public long? StringInstrumentStartAnimId { get; set; }

    public long? PercussionInstrumentStartAnimId { get; set; }

    public long? TubeInstrumentStartAnimId { get; set; }

    public long? StringInstrumentFireAnimId { get; set; }

    public long? PercussionInstrumentFireAnimId { get; set; }

    public long? TubeInstrumentFireAnimId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public byte[] DefaultGcd { get; set; }

    public byte[] ShowTargetCastingTime { get; set; }

    public byte[] ValidHeightEdgeToEdge { get; set; }

    public long? LinkEquipSlotId { get; set; }

    public long? LinkBackpackTypeId { get; set; }

    public byte[] KeepManaRegen { get; set; }

    public long? CrimePoint { get; set; }

    public byte[] LevelRuleNoConsideration { get; set; }

    public byte[] UseWeaponCooldownTime { get; set; }

    public byte[] SynergyIcon1Buffkind { get; set; }

    public long? SynergyIcon1Id { get; set; }

    public byte[] SynergyIcon2Buffkind { get; set; }

    public long? SynergyIcon2Id { get; set; }

    public long? CombatDiceId { get; set; }

    public byte[] CanActiveWeaponWithoutAnim { get; set; }

    public long? CustomGcd { get; set; }

    public byte[] CancelOngoingBuffs { get; set; }

    public long? CancelOngoingBuffExceptionTagId { get; set; }

    public byte[] SourceCannotUseWhileWalk { get; set; }

    public byte[] SourceMountMate { get; set; }

    public byte[] MatchAnimationCount { get; set; }

    public long? DualWieldFireAnimId { get; set; }

    public byte[] AutoFire { get; set; }

    public byte[] CheckTerrain { get; set; }

    public byte[] TargetOnlyWater { get; set; }

    public byte[] SourceNotSwim { get; set; }

    public byte[] TargetPreoccupied { get; set; }

    public byte[] StopChannelingOnStartSkill { get; set; }

    public byte[] StopCastingByTurn { get; set; }

    public byte[] TargetMyNpc { get; set; }

    public long? GainLifePoint { get; set; }

    public byte[] TargetFishing { get; set; }

    public byte[] SourceNoSlave { get; set; }

    public byte[] AutoReuse { get; set; }

    public long? AutoReuseDelay { get; set; }

    public byte[] SourceNotCollided { get; set; }

    public long? SkillPoints { get; set; }

    public long? DoodadHitFamily { get; set; }

    public byte[] NameTr { get; set; }

    public byte[] DescTr { get; set; }

    public byte[] WebDescTr { get; set; }

    public byte[] SensitiveOperation { get; set; }

    public byte[] FirstReagentOnly { get; set; }

    public byte[] SourceAlive { get; set; }

    public long? TargetDecalRadius { get; set; }

    public long? DoodadBundleId { get; set; }
}

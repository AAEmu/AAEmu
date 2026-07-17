using AAEmu.Game.Models.Game.Animation;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillTemplate
{
    public uint Id { get; set; }

public bool ValidHeightEdgeToEdge { get; set; }

public bool UseSkillCamera { get; set; }

public uint TwohandFireAnimId { get; set; }

public uint TubeInstrumentStartAnimId { get; set; }

public uint TubeInstrumentFireAnimId { get; set; }

public int ThirdCooldownTagId { get; set; }

public int TargetDecalRadius { get; set; }

public uint SynergyIcon2Id { get; set; }

public bool SynergyIcon2BuffKind { get; set; }

public uint SynergyIcon1Id { get; set; }

public bool SynergyIcon1BuffKind { get; set; }

public bool SwitchToSkillCooldown { get; set; }

public uint StringInstrumentStartAnimId { get; set; }

public uint StringInstrumentFireAnimId { get; set; }

public bool StopAutoattack { get; set; }

public bool StartAutoattack { get; set; }

public uint StartAnimId { get; set; }

public bool SourceShouldSwim { get; set; }

public bool SourceAlive { get; set; }

public bool SkipValidateSource { get; set; }

public bool SkipQuestApplyUseItem { get; set; }

public bool ShowTargetCastingTime { get; set; }

public bool SensitiveOperation { get; set; }

public int SecondCooldownTagId { get; set; }

public uint ProjectileId { get; set; }

public float PitchAngle { get; set; }

public uint PercussionInstrumentStartAnimId { get; set; }

public uint PercussionInstrumentFireAnimId { get; set; }

public string Name { get; set; }

public int MinHighAbilityResource { get; set; }

public int MaxHighAbilityResource { get; set; }

public bool MatchAnimationCount { get; set; }

public int LinkEquipSlotId { get; set; }

public int LinkBackpackTypeId { get; set; }

public uint IconId { get; set; }

public int HighAbilityId { get; set; }

public uint FxGroupId { get; set; }

public uint DualWieldFireAnimId { get; set; }

public uint DoodadBundleId { get; set; }

public string Desc { get; set; }

public int ControllerCameraSpeed { get; set; }

public bool ControllerCamera { get; set; }

public bool CheckObstacle { get; set; }

public int CharRaceId { get; set; }

public uint ChannelingAnimId { get; set; }

public int CategoryId { get; set; }

public bool CastingUseable { get; set; }

public bool CanActiveWeaponWithoutAnim { get; set; }

public float CameraSpeed { get; set; }

public float CameraSlowDownDistance { get; set; }

public float CameraMaxDistance { get; set; }

public bool CameraHoldZ { get; set; }

public float CameraDuration { get; set; }

public float CameraAcceleration { get; set; }

public bool CalcUserLevel { get; set; }

public bool AutoFire { get; set; }

public bool AccountCooldown { get; set; }
    public int Cost { get; set; }
    public bool Show { get; set; }
    public Anim FireAnim { get; set; }
    public AbilityType AbilityId { get; set; }
    public int ManaCost { get; set; }
    public int TimingId { get; set; }
    public int CooldownTime { get; set; }
    public int CastingTime { get; set; }
    public bool IgnoreGlobalCooldown { get; set; }
    public int EffectDelay { get; set; }
    public float EffectSpeed { get; set; }
    public int EffectRepeatCount { get; set; }
    public int EffectRepeatTick { get; set; }
    public int ActiveWeaponId { get; set; }
    public SkillTargetType TargetType { get; set; }
    public SkillTargetSelection TargetSelection { get; set; }
    public SkillTargetRelation TargetRelation { get; set; }
    public int TargetAreaCount { get; set; }
    public int TargetAreaRadius { get; set; }
    public bool TargetSiege { get; set; }
    public int WeaponSlotForAngleId { get; set; }
    public int TargetAngle { get; set; }
    public int WeaponSlotForRangeId { get; set; }
    public int WeaponSlotForAutoAttackId { get; set; }
    public int MinRange { get; set; }
    public int MaxRange { get; set; }
    public bool KeepStealth { get; set; }
    public int Aggro { get; set; }
    public int ChannelingTime { get; set; }
    public int ChannelingTick { get; set; }
    public int ChannelingMana { get; set; }
    public uint ChannelingTargetBuffId { get; set; }
    public int TargetAreaAngle { get; set; }
    public int AbilityLevel { get; set; }
    public uint ChannelingDoodadId { get; set; }
    public int CooldownTagId { get; set; }
    public uint SkillControllerId { get; set; }
    public int RepeatCount { get; set; }
    public int RepeatTick { get; set; }
    public uint ToggleBuffId { get; set; }
    public bool TargetDead { get; set; }
    public uint ChannelingBuffId { get; set; }
    public int ReagentCorpseStatusId { get; set; }
    public bool SourceDead { get; set; }
    public int LevelStep { get; set; }
    public float ValidHeight { get; set; }
    public float TargetValidHeight { get; set; }
    public bool SourceMount { get; set; }
    public bool StopCastingOnBigHit { get; set; }
    public bool StopChannelingOnBigHit { get; set; }
    public bool AutoLearn { get; set; }
    public bool NeedLearn { get; set; }
    public uint MainhandToolId { get; set; }
    public uint OffhandToolId { get; set; }
    public int FrontAngle { get; set; }
    public float ManaLevelMd { get; set; }
    public bool Unmount { get; set; }
    public uint DamageTypeId { get; set; }
    public bool AllowToPrisoner { get; set; }
    public uint MilestoneId { get; set; }
    public bool MatchAnimation { get; set; }
    public Plot Plot { get; set; }
    public bool UseAnimTime { get; set; }
    public int ConsumeLaborPower { get; set; }
    public bool SourceStun { get; set; }
    public bool TargetAlive { get; set; }
    public bool TargetWater { get; set; }
    public int CastingInc { get; set; }
    public bool CastingCancelable { get; set; }
    public bool CastingDelayable { get; set; }
    public bool ChannelingCancelable { get; set; }
    public float TargetOffsetAngle { get; set; }
    public float TargetOffsetDistance { get; set; }
    public int ActabilityGroupId { get; set; }
    public bool PlotOnly { get; set; }
    public bool SkillControllerAtEnd { get; set; }
    public bool EndSkillController { get; set; }
    public bool OrUnitReqs { get; set; }
    public bool DefaultGcd { get; set; }
    public bool KeepManaRegen { get; set; }
    public int CrimePoint { get; set; }
    public bool LevelRuleNoConsideration { get; set; }
    public bool UseWeaponCooldownTime { get; set; }
    public int CombatDiceId { get; set; }
    public int CustomGcd { get; set; }
    public bool CancelOngoingBuffs { get; set; }
    public uint CancelOngoingBuffExceptionTagId { get; set; }
    public bool SourceCannotUseWhileWalk { get; set; }
    public bool SourceMountMate { get; set; }
    public bool CheckTerrain { get; set; }
    public bool TargetOnlyWater { get; set; }
    public bool SourceNotSwim { get; set; }
    public bool TargetPreoccupied { get; set; }
    public bool StopChannelingOnStartSkill { get; set; }
    public bool StopCastingByTurn { get; set; }
    public bool TargetMyNpc { get; set; }
    public int GainLifePoint { get; set; }
    public bool TargetFishing { get; set; }
    public bool SourceNoSlave { get; set; }
    public bool AutoReUse { get; set; }
    public int AutoReUseDelay { get; set; }
    public bool SourceNotCollided { get; set; }
    public int SkillPoints { get; set; }
    public int DoodadHitFamily { get; set; }
    public List<SkillEffect> Effects { get; set; } = [];
    public bool FirstReagentOnly { get; set; }
}

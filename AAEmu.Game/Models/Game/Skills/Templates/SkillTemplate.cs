using System.Collections.Generic;
using AAEmu.Game.Models.Game.Animation;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public class SkillTemplate
    {
        public uint Id { get; set; }
        public int Cost { get; set; }
        public bool Show { get; set; }
        public Anim FireAnim { get; set; }
        public byte AbilityId { get; set; }
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
        public int SkillControllerId { get; set; }
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
        public List<SkillEffect> Effects { get; set; }

        public SkillTemplate()
        {
            Effects = new List<SkillEffect>();
        }
    }
}

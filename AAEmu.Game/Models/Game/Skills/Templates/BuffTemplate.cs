using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class BuffTemplate
{
    public uint Id { get; init; }
    public uint BuffId => Id;
    public uint AnimStartId { get; init; }
    public uint AnimEndId { get; init; }
    public int Duration { get; init; }
    public double Tick { get; init; }
    public bool Silence { get; init; }
    public bool Root { get; init; }
    public bool Sleep { get; init; }
    public bool Stun { get; init; }
    public bool Cripled { get; init; }
    public bool Stealth { get; init; }
    public bool RemoveOnSourceDead { get; init; }
    public uint LinkBuffId { get; init; }
    public int TickManaCost { get; init; }
    public BuffStackRule StackRule { get; init; }
    public int InitMinCharge { get; init; }
    public int InitMaxCharge { get; init; }
    public int MaxStack { get; init; }
    public uint DamageAbsorptionTypeId { get; init; }
    public int DamageAbsorptionPerHit { get; init; }
    public int AuraRadius { get; init; }
    public int ManaShieldRatio { get; init; }
    public bool FrameHold { get; init; }
    public bool Ragdoll { get; init; }
    public bool OneTime { get; init; }
    public int ReflectionChance { get; init; }
    public uint ReflectionTypeId { get; init; }
    public uint RequireBuffId { get; init; }
    public bool Taunt { get; init; }
    public bool TauntWithTopAggro { get; init; }
    public bool RemoveOnUseSkill { get; init; }
    public bool MeleeImmune { get; init; }
    public bool SpellImmune { get; init; }
    public bool RangedImmune { get; init; }
    public bool SiegeImmune { get; init; }
    public int ImmuneDamage { get; init; }
    public uint SkillControllerId { get; init; }
    public int ResurrectionHealth { get; init; }
    public int ResurrectionMana { get; init; }
    public bool ResurrectionPercent { get; init; }
    public int LevelDuration { get; init; }
    public int ReflectionRatio { get; init; }
    public int ReflectionTargetRatio { get; init; }
    public bool KnockbackImmune { get; init; }
    public uint ImmuneBuffTagId { get; init; }
    public uint AuraRelationId { get; init; }
    public uint GroupId { get; init; }
    public int GroupRank { get; init; }
    public bool PerUnitCreation { get; init; }
    public float TickAreaRadius { get; init; }
    public uint TickAreaRelationId { get; init; }
    public bool RemoveOnMove { get; init; }
    public bool UseSourceFaction { get; init; }
    public FactionsEnum FactionId { get; init; }
    public bool Exempt { get; init; }
    public int TickAreaFrontAngle { get; init; }
    public int TickAreaAngle { get; init; }
    public bool Psychokinesis { get; init; }
    public bool NoCollide { get; init; }
    public float PsychokinesisSpeed { get; init; }
    public bool RemoveOnDeath { get; init; }
    public uint TickAnimId { get; init; }
    public uint TickActiveWeaponId { get; init; }
    public bool ConditionalTick { get; init; }
    public bool System { get; init; }
    public uint AuraSlaveBuffId { get; init; }
    public bool NonPushable { get; init; }
    public uint ActiveWeaponId { get; init; }
    public int MaxCharge { get; init; }
    public bool DetectStealth { get; init; }
    public bool RemoveOnExempt { get; init; }
    public bool RemoveOnLand { get; init; }
    public bool Gliding { get; init; }
    public int GlidingRotateSpeed { get; init; }
    public bool Knockdown { get; init; }
    public bool TickAreaExcludeSource { get; init; }
    public bool FallDamageImmune { get; init; }
    public BuffKind Kind { get; init; }
    public uint TransformBuffId { get; init; }
    public bool BlankMinded { get; init; }
    public bool Fastened { get; init; }
    public bool SlaveApplicable { get; init; }
    public bool Pacifist { get; init; }
    public bool RemoveOnInteraction { get; init; }
    public bool Crime { get; init; }
    public bool RemoveOnUnmount { get; init; }
    public bool AuraChildOnly { get; init; }
    public bool RemoveOnMount { get; init; }
    public bool RemoveOnStartSkill { get; init; }
    public bool SprintMotion { get; init; }
    public float TelescopeRange { get; init; }
    public uint MainhandToolId { get; init; }
    public uint OffhandToolId { get; init; }
    public uint TickMainhandToolId { get; init; }
    public uint TickOffhandToolId { get; init; }
    public float TickLevelManaCost { get; init; }
    public bool WalkOnly { get; init; }
    public bool CannotJump { get; init; }
    public uint CrowdBuffId { get; init; }
    public float CrowdRadius { get; init; }
    public int CrowdNumber { get; init; }
    public bool EvadeTelescope { get; init; }
    public float TransferTelescopeRange { get; init; }
    public bool RemoveOnAttackSpellDot { get; init; }
    public bool RemoveOnAttackEtcDot { get; init; }
    public bool RemoveOnAttackBuffTrigger { get; init; }
    public bool RemoveOnAttackEtc { get; init; }
    public bool RemoveOnAttackedSpellDot { get; init; }
    public bool RemoveOnAttackedEtcDot { get; init; }
    public bool RemoveOnAttackedBuffTrigger { get; init; }
    public bool RemoveOnAttackedEtc { get; init; }
    public bool RemoveOnDamageSpellDot { get; init; }
    public bool RemoveOnDamageEtcDot { get; init; }
    public bool RemoveOnDamageBuffTrigger { get; init; }
    public bool RemoveOnDamageEtc { get; init; }
    public bool RemoveOnDamagedSpellDot { get; init; }
    public bool RemoveOnDamagedEtcDot { get; init; }
    public bool RemoveOnDamagedBuffTrigger { get; init; }
    public bool RemoveOnDamagedEtc { get; init; }
    public bool OwnerOnly { get; init; }
    public bool RemoveOnAutoAttack { get; init; }
    public BuffSaveRuleType SaveRuleId { get; init; }
    public bool AntiStealth { get; init; }
    public float Scale { get; init; }
    public float ScaleDuration { get; init; }
    public bool ImmuneExceptCreator { get; init; }
    public uint ImmuneExceptSkillTagId { get; init; }
    public float FindSchoolOfFishRange { get; init; }
    public uint AnimActionId { get; init; }
    public bool DeadApplicable { get; init; }
    public bool TickAreaUseOriginSource { get; init; }
    public bool RealTime { get; init; }
    public bool DoNotRemoveByOtherSkillController { get; init; }
    public uint CooldownSkillId { get; init; }
    public int CooldownSkillTime { get; init; }
    public bool ManaBurnImmune { get; init; }
    public bool FreezeShip { get; init; }
    public bool CrowdFriendly { get; init; }
    public bool CrowdHostile { get; init; }
    public bool OnActionTime => Tick > 0;

    public List<TickEffect> TickEffects { get; } = [];
    public List<BonusTemplate> Bonuses { get; } = [];
    public List<DynamicBonusTemplate> DynamicBonuses { get; } = [];

    public void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (BuffId > 0 && target.Buffs.CheckBuff(BuffId))
            return;

        if (RequireBuffId > 0 && !target.Buffs.CheckBuff(RequireBuffId))
            return; //TODO send error?

        if (target.Buffs.CheckBuffImmune(Id))
            return; //TODO  error of immune?

        uint abLevel = 1;

        if (caster is Character character)
        {
            if (source.Skill != null)
            {
                var template = source.Skill.Template;
                var abilityLevel = character.GetAbLevel(source.Skill.Template.AbilityId);
                if (template.LevelStep != 0)
                    abLevel = (uint)(abilityLevel / template.LevelStep * template.LevelStep);
                else
                    abLevel = (uint)template.AbilityLevel;

                //Dont allow lower than minimum ablevel for skill or infinite debuffs can happen
                abLevel = (uint)Math.Max(template.AbilityLevel, (int)abLevel);
            }
            else if (source.Buff != null)
            {
                //not sure?
            }
        }
        else
        {
            if (source.Skill != null)
            {
                abLevel = (uint)source.Skill.Template.AbilityLevel;
            }
        }
        target.Buffs.AddBuff(new Buff(target, caster, casterObj, this, source.Skill, time) { AbLevel = abLevel });
    }

    public void Start(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        foreach (var template in Bonuses)
        {
            var bonus = new Bonus { Template = template, Value = (int)Math.Round(template.Value + template.LinearLevelBonus * (buff.AbLevel / 100f)) };
            owner.AddBonus(buff.Index, bonus);
        }

        // dynamic_unit_modifiers handling.
        // Two cases:
        //  (A) Static dynamic: resolved once via abLevel (PR #1433 behavior, e.g. buff 1895 +1040 MaxMana).
        //  (B) Time-evolving dynamic: the LinearFunc describes start_value -> end_value over the buff
        //      duration (e.g. buff 2504 "저주의 시선" with -50 -> -150 MeleeSpeedMul over 10s,
        //      or buff 114 "현기증" with -250 -> -600 over 3s). For (B), the bonus value is recomputed
        //      every tick in TimeToTimeApply.
        // Heuristic for (B): actualDuration > 0 AND Tick > 0 AND the LinearFunc has
        // start_value != end_value. Otherwise treat as (A).
        //
        // IMPORTANT: actualDuration is computed via GetDuration(buff.AbLevel) — NOT the raw DB
        // Duration field. Some buffs use LevelDuration so that the effective duration is
        // (LevelDuration * abLevel + Duration). buff.Duration is not yet set at this point in
        // the flow (it gets assigned by UpdateEffect/ScheduleEffect just after Start returns),
        // so we recompute it here.
        var actualDuration = GetDuration(buff.AbLevel);
        var hasDuration = actualDuration > 0 && Tick > 0;
        foreach (var template in DynamicBonuses)
        {
            int initialValue;
            bool evolves = false;

            if (hasDuration && IsTimeEvolvingDynamic(template))
            {
                // Start at t=0 -> start_value
                initialValue = SkillManager.Instance.ResolveDynamicBonusValueTime(template, 0L, actualDuration);
                evolves = true;
            }
            else
            {
                initialValue = SkillManager.Instance.ResolveDynamicBonusValue(template, buff.AbLevel);
            }

            var bonusTemplate = new BonusTemplate
            {
                Attribute = template.Attribute,
                ModifierType = template.ModifierType,
                Value = initialValue,
                LinearLevelBonus = 0
            };
            var bonus = new Bonus { Template = bonusTemplate, Value = initialValue };
            owner.AddBonus(buff.Index, bonus);

            if (evolves)
                buff.EvolvingDynamicBonuses.Add((template, bonus));
        }

        if (buff.Charge == 0)
            buff.Charge = Random.Shared.Next(InitMinCharge, InitMaxCharge);

        if (!buff.Passive)
            owner.BroadcastPacket(new SCBuffCreatedPacket(buff), true);

        // Special properties handling
        if (owner is Character character)
        {
            if (FindSchoolOfFishRange > 0)
                RadarManager.Instance.RegisterForFishSchool(character, FindSchoolOfFishRange);
            if (TransferTelescopeRange > 0)
                RadarManager.Instance.RegisterForPublicTransport(character, TransferTelescopeRange);
            if (TelescopeRange > 0)
                RadarManager.Instance.RegisterForShips(character, TelescopeRange);
            if (character.Buffs.CheckBuff((uint)BuffConstants.Dash))
            {
                var template = new ManaRegenTemplate(character, buff.Template.Tick, buff.Template.TickLevelManaCost, character.Level);
                ManaRegenManager.Instance.Register(character, template);
            }
        }
    }

    /// <summary>
    /// Returns true when the dynamic_unit_modifier references a LinearFunc whose start_value
    /// differs from its end_value, indicating the value evolves over the buff duration rather
    /// than being a flat snapshot at apply time.
    /// </summary>
    private static bool IsTimeEvolvingDynamic(DynamicBonusTemplate template)
    {
        if (template == null)
            return false;

        var funcType = template.FuncType?.Replace("_", string.Empty).ToLowerInvariant();
        if (funcType != "linearfunc" && funcType != "manualfunc")
            return false;

        // Probe: t=0 returns StartValue, t=duration returns EndValue. If they differ, the function evolves.
        // We use any positive duration; only the ratio matters for the comparison.
        var atStart = SkillManager.Instance.ResolveDynamicBonusValueTime(template, 0L, 1L);
        var atEnd = SkillManager.Instance.ResolveDynamicBonusValueTime(template, 1L, 1L);
        return atStart != atEnd;
    }

    public void TimeToTimeApply(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        // Refresh time-evolving dynamic bonuses (e.g. buff 2504, buff 114).
        // We mutate bonus.Value in place so that CalculateWithBonuses on the next stat read picks
        // up the new value. The Bonus object kept in EvolvingDynamicBonuses is the same instance
        // stored in owner.Bonuses[buff.Index], so this update is visible without re-registering
        // (which would risk stacking on the same buff.Index).
        //
        // IMPORTANT: use buff.Duration (set by UpdateEffect/ScheduleEffect right after Start
        // returned, equal to GetDuration(AbLevel)) rather than the raw BuffTemplate.Duration,
        // so that the interpolation aligns with the actual buff lifetime even for buffs that
        // use LevelDuration.
        if (buff.EvolvingDynamicBonuses.Count > 0 && buff.Duration > 0)
        {
            var elapsed = (long)buff.GetTimeElapsed();
            foreach (var (template, bonus) in buff.EvolvingDynamicBonuses)
            {
                var newValue = SkillManager.Instance.ResolveDynamicBonusValueTime(template, elapsed, buff.Duration);
                bonus.Value = newValue;
                if (bonus.Template != null)
                    bonus.Template.Value = newValue;
            }
        }

        if (TickAreaRadius > 0)
        {
            DoAreaTick(caster, owner, buff);
            return;
        }

        foreach (var tickEff in TickEffects)
        {
            if (tickEff.TargetBuffTagId > 0 &&
                !owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetBuffTagId)))
                continue;
            if (tickEff.TargetNoBuffTagId > 0 &&
                owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetNoBuffTagId)))
                continue;

            var eff = SkillManager.Instance.GetEffectTemplate(tickEff.EffectId);
            if (eff == null)
            {
                continue;
            }

            var targetObj = new SkillCastUnitTarget(owner.ObjId);
            var skillObj = new SkillObject(); // TODO ?
            eff.Apply(caster, buff.SkillCaster, owner, targetObj, new CastBuff(buff), new EffectSource(this), skillObj,
                DateTime.UtcNow);
        }
    }

    private void DoAreaTick(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        var units = WorldManager.GetAround<Unit>(owner, TickAreaRadius);

        owner ??= caster;

        var ownerUnit = owner as Unit;
        if (TickAreaExcludeSource && ownerUnit != null)
            units.Remove(ownerUnit);
        else if (ownerUnit != null && !units.Contains(owner))
            units.Add(ownerUnit);

        units = SkillTargetingUtil.FilterWithRelation((SkillTargetRelation)TickAreaRelationId, (Unit)caster, units).ToList();

        var source = caster;
        //if (TickAreaUseOriginSource)
        //source = (Unit)owner;
        var skillObj = new SkillObject(); // TODO ?

        // Create a copy of the units collection for safe iteration
        var unitsCopy = units.ToList();
        //lock (units)
        {
            foreach (var tickEff in TickEffects)
            {
                var eff = SkillManager.Instance.GetEffectTemplate(tickEff.EffectId);

                foreach (var trg in unitsCopy)
                //foreach (var trg in units)
                {
                    if (tickEff.TargetBuffTagId > 0 &&
                        !trg.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetBuffTagId)))
                        continue;
                    if (tickEff.TargetNoBuffTagId > 0 &&
                        trg.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetNoBuffTagId)))
                        continue;

                    var targetObj = new SkillCastUnitTarget(trg.ObjId);
                    eff.Apply(source, buff.SkillCaster, trg, targetObj, new CastBuff(buff), new EffectSource(this), skillObj, DateTime.UtcNow);
                }
            }
        }
    }

    public void Dispel(BaseUnit caster, BaseUnit owner, Buff buff, bool replaced = false)
    {
        foreach (var template in Bonuses)
            owner.RemoveBonus(buff.Index, template.Attribute);
        foreach (var template in DynamicBonuses)
            owner.RemoveBonus(buff.Index, template.Attribute);
        buff.EvolvingDynamicBonuses.Clear();
        var requiringBuffs = owner.Buffs.GetBuffsRequiring(buff.Template.Id);
        foreach (var requiringBuff in requiringBuffs.ToList())
            requiringBuff.Exit();

        if (!buff.Passive && !replaced)
            owner.BroadcastPacket(new SCBuffRemovedPacket(owner.ObjId, buff.Index), true);

        // Special properties handling
        if (owner is Character character)
        {
            if (FindSchoolOfFishRange > 0)
                RadarManager.Instance.RegisterForFishSchool(character, 0f);
            if (TransferTelescopeRange > 0)
                RadarManager.Instance.RegisterForPublicTransport(character, 0f);
            if (TelescopeRange > 0)
                RadarManager.Instance.RegisterForShips(character, 0f);
        }
    }

    public void WriteData(PacketStream stream, uint abLevel)
    {
        stream.WritePisc(0, GetDuration(abLevel) / 10, 0, (long)(Tick / 10)); // unk, Duration, unk / 10, Tick
    }

    public int GetDuration(uint abLevel)
    {
        return Math.Max(0, LevelDuration * (int)abLevel + Duration);
    }

    public double GetTick()
    {
        return Tick;
    }
}

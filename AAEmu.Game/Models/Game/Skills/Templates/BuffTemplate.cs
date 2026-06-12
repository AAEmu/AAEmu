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
using NLog;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class BuffTemplate
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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
    public float GlidingLiftHeight { get; init; }
    public float GlidingLiftSpeed { get; init; }
    public float GlidingLiftDuration { get; init; }
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

    /// <summary>
    /// Removes the static and dynamic unit bonuses this buff registered under <paramref name="buff"/>.Index.
    /// Shared by <see cref="Start"/> (to stay idempotent across refreshes) and <see cref="Dispel"/>.
    /// </summary>
    private void RemoveBonuses(BaseUnit owner, Buff buff)
    {
        foreach (var template in Bonuses)
            owner.RemoveBonus(buff.Index, template.Attribute);
        foreach (var template in DynamicBonuses)
            owner.RemoveDynamicBonus(buff.Index, template.Attribute);
    }

    public void Start(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        RemoveBonuses(owner, buff);

        foreach (var template in Bonuses)
        {
            var bonus = new Bonus { Template = template, Value = (int)Math.Round(template.Value + template.LinearLevelBonus * (buff.AbLevel / 100f)) };
            owner.AddBonus(buff.Index, bonus);
        }

        // dynamic_unit_modifiers: register as time-evaluated DynamicBonus tied to the source buff
        // (NOT snapshotted here). The value is computed on the fly in Unit.CalculateWithBonuses.
        foreach (var template in DynamicBonuses)
        {
            switch (template.FuncType)
            {
                case "LinearFunc":
                {
                    var linearFunc = SkillManager.Instance.GetLinearFunc(template.FuncId);
                    if (linearFunc == null)
                    {
                        Logger.Warn($"Missing linear_func {template.FuncId} for dynamic_unit_modifier on buff {Id}.");
                        continue;
                    }

                    var dynamicBonus = new DynamicBonus
                    {
                        Template = template,
                        SourceBuff = buff,
                        LinearFunc = linearFunc
                    };
                    owner.AddDynamicBonus(buff.Index, dynamicBonus);
                    break;
                }

                case "ManualFunc":
                    // Not implemented: don't silently apply a wrong value.
                    Logger.Warn($"ManualFunc dynamic_unit_modifier not implemented (func_id={template.FuncId}, buff {Id}).");
                    break;

                default:
                    Logger.Warn($"Unsupported dynamic_unit_modifier FuncType={template.FuncType}, FuncId={template.FuncId}, buff {Id}.");
                    break;
            }
        }

        if (buff.Charge == 0)
            buff.Charge = Random.Shared.Next(InitMinCharge, InitMaxCharge);

        if (!buff.Passive)
            owner.BroadcastPacket(new SCBuffCreatedPacket(buff), true);

        // Buff-driven SkillController for NPCs: pull / lift / fear / leap / dash
        // skills carry their displacement via a buff. SkillControllerId resolves
        // to a SkillControllerTemplate whose KindId picks the controller class
        // (Floating, Wandering, Leap, Dash). Skip Characters — the 1.2 client
        // moves itself from the buff payload and running the server-side SC on
        // top of that produces double-displacement.
        if (SkillControllerId > 0 && owner is Unit ownerUnit && owner is not Character && caster != null)
        {
            var scTemplate = SkillManager.Instance.GetEffectTemplate(SkillControllerId, "SkillController")
                as SkillControllerTemplate;
            if (scTemplate != null)
            {
                // Bubbletrap-style buffs set Gliding=true but leave GlidingLiftHeight=0
                // in the DB, which would make the controller resolve to pull mode.
                // Default to 5m lift so the controller actually enters lift mode.
                var effectiveLiftHeight = GlidingLiftHeight > 0f ? GlidingLiftHeight
                    : (Gliding ? 5f : 0f);
                Logger.Debug("BuffTemplate.Start: buff {0} creating SC sc_id={1} kind={2} for owner={3} caster={4} psychoSpeed={5} liftH={6}",
                    Id, SkillControllerId, scTemplate.KindId, owner.ObjId, caster.ObjId, PsychokinesisSpeed, effectiveLiftHeight);
                var sc = SkillControllers.SkillController.CreateSkillController(scTemplate, owner, caster,
                    PsychokinesisSpeed, effectiveLiftHeight, GlidingLiftSpeed, GlidingLiftDuration);
#pragma warning disable CA1508 // Factory can return null for unimplemented controller kinds
                if (sc is not null)
#pragma warning restore CA1508
                {
                    sc.SourceBuffId = Id;
                    if (ownerUnit.ActiveSkillController != null)
                        ownerUnit.ActiveSkillController.End(force: true);
                    ownerUnit.ActiveSkillController = sc;
                    sc.Execute();
                }
                else
                {
                    Logger.Warn("BuffTemplate.Start: buff {0} SC factory returned null for kind={1} — controller class not implemented", Id, scTemplate.KindId);
                }
            }
            else
            {
                Logger.Warn("BuffTemplate.Start: buff {0} SC template not found for sc_id={1}", Id, SkillControllerId);
            }
        }
        // Gliding/Bubbletrap fallback: buffs that set Gliding=true (sometimes
        // with no SkillControllerId at all in the data) should lift the target
        // up to GlidingLiftHeight. Without this branch a Gliding-only buff
        // would fall through to the Psychokinesis pull below and incorrectly
        // pull the target toward the caster instead of lifting it.
        else if (SkillControllerId == 0 && Gliding
                 && owner is Unit liftUnit && owner is not Character && caster != null)
        {
            var liftHeight = GlidingLiftHeight > 0f ? GlidingLiftHeight : 5f;
            var liftSpeed = GlidingLiftSpeed > 0f ? GlidingLiftSpeed : 3f;
            Logger.Debug("BuffTemplate.Start: buff {0} Gliding lift (no SC id) for NPC owner={1} height={2} speed={3} duration={4}",
                Id, owner.ObjId, liftHeight, liftSpeed, GlidingLiftDuration);
            var sc = new SkillControllers.FloatingSkillController(null, owner, caster, 0f, liftHeight, liftSpeed, GlidingLiftDuration);
            sc.SourceBuffId = Id;
            if (liftUnit.ActiveSkillController != null)
                liftUnit.ActiveSkillController.End(force: true);
            liftUnit.ActiveSkillController = sc;
            sc.Execute();
        }
        // Psychokinesis fallback: some pull buffs use Psychokinesis=true +
        // PsychokinesisSpeed instead of a full SkillControllerId. Spawn a
        // FloatingSkillController in pull mode directly.
        else if (SkillControllerId == 0 && Psychokinesis && PsychokinesisSpeed > 0
                 && owner is Unit psychoUnit && owner is not Character && caster != null)
        {
            Logger.Debug("BuffTemplate.Start: buff {0} Psychokinesis pull (no SC id) for NPC owner={1} toward caster={2} speed={3}",
                Id, owner.ObjId, caster.ObjId, PsychokinesisSpeed);
            var sc = new SkillControllers.FloatingSkillController(null, owner, caster, PsychokinesisSpeed);
            sc.SourceBuffId = Id;
            if (psychoUnit.ActiveSkillController != null)
                psychoUnit.ActiveSkillController.End(force: true);
            psychoUnit.ActiveSkillController = sc;
            sc.Execute();
        }

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

    public void TimeToTimeApply(BaseUnit caster, BaseUnit owner, Buff buff)
    {
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
        // Stop the SkillController that this buff started. End() may keep
        // State=Running for Floating's fall phase, so we deliberately do NOT
        // null ActiveSkillController here — the controller clears itself
        // from Unit on FinalEnd after the descent lands.
        if (owner is Unit dispelUnit
            && dispelUnit.ActiveSkillController != null
            && dispelUnit.ActiveSkillController.SourceBuffId == Id)
        {
            dispelUnit.ActiveSkillController.End();
        }
        DispelCore(caster, owner, buff, replaced);
    }

    private void DispelCore(BaseUnit caster, BaseUnit owner, Buff buff, bool replaced)
    {
        RemoveBonuses(owner, buff);
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

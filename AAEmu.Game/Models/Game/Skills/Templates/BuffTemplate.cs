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

    // -------------------------------------------------------------------------
    // Идентификаторы
    // -------------------------------------------------------------------------
    public uint Id { get; set; }
    public uint BuffId => Id;
    public string Name { get; set; }
    public string Desc { get; set; }
    public uint IconId { get; set; }
    public uint GroupId { get; set; }
    public uint GroupRank { get; set; }
    public uint KindId { get; set; }
    public BuffKind Kind { get; set; }

    // -------------------------------------------------------------------------
    // Тайминг
    // -------------------------------------------------------------------------
    public int Duration { get; set; }
    public int LevelDuration { get; set; }
    public uint MaxLifeTime { get; set; }

    // -------------------------------------------------------------------------
    // Стекинг и заряды
    // -------------------------------------------------------------------------
    public uint MaxStack { get; set; }
    public uint StackRuleId { get; set; }
    public BuffStackRule StackRule { get; set; }
    public int InitMinCharge { get; set; }
    public int InitMaxCharge { get; set; }
    public int MaxCharge { get; set; }

    // -------------------------------------------------------------------------
    // Тики
    // -------------------------------------------------------------------------
    public double Tick { get; set; }
    public bool OnActionTime => Tick > 0;
    public uint TickAnimId { get; set; }
    public uint TickActiveWeaponId { get; set; }
    public uint TickMainhandToolId { get; set; }
    public uint TickOffhandToolId { get; set; }
    public uint TickManaCost { get; set; }
    public float TickLevelManaCost { get; set; }
    public uint TickAreaRelationId { get; set; }
    public float TickAreaRadius { get; set; }
    public uint TickAreaAngle { get; set; }
    public uint TickAreaFrontAngle { get; set; }
    public bool TickAreaExcludeSource { get; set; }
    public bool TickAreaUseOriginSource { get; set; }
    public bool ConditionalTick { get; set; }

    // -------------------------------------------------------------------------
    // Анимации
    // -------------------------------------------------------------------------
    public uint AnimStartId { get; set; }
    public uint AnimEndId { get; set; }
    public uint AnimActionId { get; set; }
    public string IdleAnim { get; set; }
    public string AgStance { get; set; }
    public string ExtraEffects { get; set; }

    // -------------------------------------------------------------------------
    // Инструменты / оружие
    // -------------------------------------------------------------------------
    public uint ActiveWeaponId { get; set; }
    public uint MainhandToolId { get; set; }
    public uint OffhandToolId { get; set; }
    public uint PercussionInstrumentStartAnimId { get; set; }
    public uint PercussionInstrumentTickAnimId { get; set; }
    public uint StringInstrumentStartAnimId { get; set; }
    public uint StringInstrumentTickAnimId { get; set; }
    public uint TubeInstrumentStartAnimId { get; set; }
    public uint TubeInstrumentTickAnimId { get; set; }

    // -------------------------------------------------------------------------
    // Фракции и таргетинг
    // -------------------------------------------------------------------------
    public FactionsEnum FactionId { get; set; }
    public bool UseSourceFaction { get; set; }
    public uint TargetingRelationId { get; set; }
    public bool TargetingUseOriginSource { get; set; }
    public bool ImpossibleTargeting { get; set; }
    public bool ImpossibleChangeTargeting { get; set; }
    public uint AuraRelationId { get; set; }

    // -------------------------------------------------------------------------
    // Ауры и слейвы
    // -------------------------------------------------------------------------
    public bool AuraChildOnly { get; set; }
    public bool AuraCreatorOnly { get; set; }
    public uint AuraRadius { get; set; }
    public uint AuraSlaveBuffId { get; set; }
    public bool SlaveApplicable { get; set; }
    public bool NotToSlaveRider { get; set; }
    public bool OwnerOnly { get; set; }
    public bool OnlyMyPet { get; set; }
    public bool OnlyPetOwner { get; set; }
    public bool PerUnitCreation { get; set; }

    // -------------------------------------------------------------------------
    // Taunt / Crowd
    // -------------------------------------------------------------------------
    public bool Taunt { get; set; }
    public bool TauntWithTopAggro { get; set; }
    public uint CrowdBuffId { get; set; }
    public bool CrowdFriendly { get; set; }
    public bool CrowdHostile { get; set; }
    public uint CrowdNumber { get; set; }
    public float CrowdRadius { get; set; }

    // -------------------------------------------------------------------------
    // Специальные свойства движения
    // -------------------------------------------------------------------------
    public bool Gliding { get; set; }
    public float GlidingFallSpeedFast { get; set; }
    public float GlidingFallSpeedNormal { get; set; }
    public float GlidingFallSpeedSlow { get; set; }
    public float GlidingLandHeight { get; set; }
    public uint GlidingLiftCount { get; set; }
    public float GlidingLiftDuration { get; set; }
    public float GlidingLiftHeight { get; set; }
    public float GlidingLiftSpeed { get; set; }
    public float GlidingLiftValidTime { get; set; }
    public float GlidingMoveSpeedFast { get; set; }
    public float GlidingMoveSpeedNormal { get; set; }
    public float GlidingMoveSpeedSlow { get; set; }
    public uint GlidingRotateSpeed { get; set; }
    public float GlidingSlidingTime { get; set; }
    public float GlidingSmoothTime { get; set; }
    public float GlidingStartupSpeed { get; set; }
    public float GlidingStartupTime { get; set; }
    public bool SprintMotion { get; set; }
    public bool WalkOnly { get; set; }
    public bool CannotJump { get; set; }

    // -------------------------------------------------------------------------
    // Телескоп / радар
    // -------------------------------------------------------------------------
    public float TelescopeRange { get; set; }
    public float TransferTelescopeRange { get; set; }
    public float BossTelescopeRange { get; set; }
    public bool EvadeTelescope { get; set; }
    public float FindSchoolOfFishRange { get; set; }

    // -------------------------------------------------------------------------
    // Иммунитеты
    // -------------------------------------------------------------------------
    public uint ImmuneBuffTagId { get; set; } // TODO: no DB column, stale
    public uint ImmuneDamage { get; set; }
    public bool ImmuneExceptCreator { get; set; }
    public uint ImmuneExceptCreatorRelationId { get; set; }
    public bool ImmuneExceptCreatorRelationCheck { get; set; }
    public uint ImmuneExceptSkillTagId { get; set; }
    public float ImmuneHealth { get; set; }
    public bool MeleeImmune { get; set; }
    public bool RangedImmune { get; set; }
    public bool SpellImmune { get; set; }
    public bool SiegeImmune { get; set; }
    public bool ManaBurnImmune { get; set; }
    public bool KnockbackImmune { get; set; }

    // -------------------------------------------------------------------------
    // Бессмертия
    // -------------------------------------------------------------------------
    public bool MeleeImmortality { get; set; }
    public bool RangedImmortality { get; set; }
    public bool SpellImmortality { get; set; }
    public bool SiegeImmortality { get; set; }
    public bool OneTimeImmortality { get; set; }
    public bool FallDamageImmortality { get; set; }

    // -------------------------------------------------------------------------
    // CC эффекты
    // -------------------------------------------------------------------------
    public bool Stun { get; set; }
    public bool Sleep { get; set; }
    public bool Root { get; set; }
    public bool Silence { get; set; }
    public bool KnockDown { get; set; }
    public bool Pacifist { get; set; }
    public bool Crippled { get; set; }
    public bool Cripled { get; set; } // 1.2 legacy alias (typo) used by CharacterCombat
    public bool Knockdown { get; set; } // 1.2 CC mechanic; knock_down column absent in 3.5 DB, stays false
    public bool Ragdoll { get; set; }
    public bool Fastened { get; set; }
    public bool BlankMinded { get; set; }
    public bool Framehold { get; set; }
    public bool Psychokinesis { get; set; }
    public float PsychokinesisSpeed { get; set; }

    // -------------------------------------------------------------------------
    // Удаление при событиях
    // -------------------------------------------------------------------------
    public bool RemoveOnDeath { get; set; }
    public bool RemoveOnMove { get; set; }
    public bool RemoveOnLand { get; set; }
    public bool RemoveOnMount { get; set; }
    public bool RemoveOnUnmount { get; set; }
    public uint RemoveOnUnmountAttachPointId { get; set; }
    public bool RemoveOnInteraction { get; set; }
    public bool RemoveOnStartSkill { get; set; }
    public bool RemoveOnUseSkill { get; set; }
    public bool RemoveOnAutoAttack { get; set; }
    public bool RemoveOnSourceDead { get; set; }
    public bool RemoveOnExempt { get; set; }
    public bool RemoveOnUnbond { get; set; }
    public bool RemoveOnAttackBuffTrigger { get; set; }
    public bool RemoveOnAttackedBuffTrigger { get; set; }
    public bool RemoveOnDamageBuffTrigger { get; set; }
    public bool RemoveOnDamagedBuffTrigger { get; set; }
    public bool RemoveOnAttackEtc { get; set; }
    public bool RemoveOnAttackedEtc { get; set; }
    public bool RemoveOnDamageEtc { get; set; }
    public bool RemoveOnDamagedEtc { get; set; }
    public bool RemoveOnAttackEtcDot { get; set; }
    public bool RemoveOnAttackedEtcDot { get; set; }
    public bool RemoveOnDamageEtcDot { get; set; }
    public bool RemoveOnDamagedEtcDot { get; set; }
    public bool RemoveOnAttackSpellDot { get; set; }
    public bool RemoveOnAttackedSpellDot { get; set; }
    public bool RemoveOnDamageSpellDot { get; set; }
    public bool RemoveOnDamagedSpellDot { get; set; }

    // -------------------------------------------------------------------------
    // Прочие флаги
    // -------------------------------------------------------------------------
    public bool Stealth { get; set; }
    public bool AntiStealth { get; set; }
    public bool DetectStealth { get; set; }
    public bool Exempt { get; set; }
    public bool System { get; set; }
    public bool RealTime { get; set; }
    public bool OneTime { get; set; }
    public bool Passive { get; set; }  // вычисляется снаружи, но хранится здесь
    public bool OffPassive { get; set; }
    public uint OffPassiveExectionTagId { get; set; }
    public bool DeadApplicable { get; set; }
    public bool NoExpPenalty { get; set; }
    public bool NonPushable { get; set; }
    public bool NoCollide { get; set; }
    public bool NoCollideRigid { get; set; }
    public bool FallDamageImmune { get; set; }
    public bool FreezeShip { get; set; }
    public bool StopOnlineLpRegen { get; set; }
    public bool RestrictActionbar { get; set; }
    public bool DoNotRemoveByOtherSkillController { get; set; }
    public bool CombatTextStart { get; set; }
    public bool CombatTextEnd { get; set; }
    public bool FixAbilityLevelToOne { get; set; }
    public float Scale { get; set; }
    public float ScaleDuration { get; set; }
    public uint FxGroupId { get; set; }
    public uint TransformBuffId { get; set; }
    public uint LinkBuffId { get; set; }
    public uint RequireBuffId { get; set; }
    public uint CooldownSkillId { get; set; }
    public uint CooldownSkillTime { get; set; }
    public uint SkillControllerId { get; set; }
    public uint SaveRuleId { get; set; }
    public uint BalanceLevel { get; set; }
    public uint AddDurationBuffMul { get; set; }
    public uint AddDurationBuffId { get; set; }
    public uint CustomDualMaterialId { get; set; }
    public float CustomDualMaterialFadeTime { get; set; }
    public uint ManaShieldRatio { get; set; }
    public uint DamageAbsorptionPerHit { get; set; }
    public uint DamageAbsorptionTypeId { get; set; }
    public uint ReflectionChance { get; set; }
    public uint ReflectionRatio { get; set; }
    public uint ReflectionTargetRatio { get; set; }
    public uint ReflectionTypeId { get; set; }
    public uint ResurrectionHealth { get; set; }
    public uint ResurrectionMana { get; set; }
    public bool ResurrectionPercent { get; set; }
    public uint MinHighAbilityResource { get; set; }
    public uint MaxHighAbilityResource { get; set; }
    public bool DisarmamentMainHand { get; set; }
    public bool DisarmamentOffHand { get; set; }
    public bool DisarmamentRanged { get; set; }
    public bool DisarmamentMusical { get; set; }
    public bool Taunt2 { get; set; } // TODO: no DB column, stale (TauntWithTopAggro alias if needed)

    // -------------------------------------------------------------------------
    // Коллекции
    // -------------------------------------------------------------------------
    public List<TickEffect> TickEffects { get; } = [];
    public List<BonusTemplate> Bonuses { get; } = [];
    public List<DynamicBonusTemplate> DynamicBonuses { get; } = [];


    // =========================================================================
    // Публичный API (фасад — делегирует обработчикам)
    // =========================================================================

    /// <summary>
    /// Применяет бафф к цели. Делегирует <see cref="BuffApplyHandler"/>.
    /// </summary>
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

        ushort abLevel = 1;

        if (caster is Character character)
        {
            if (source.Skill != null)
            {
                var template = source.Skill.Template;
                var abilityLevel = character.GetAbLevel(source.Skill.Template.AbilityId);
                if (template.LevelStep != 0)
                    abLevel = (ushort)(abilityLevel / template.LevelStep * template.LevelStep);
                else
                    abLevel = (ushort)template.AbilityLevel;

                //Dont allow lower than minimum ablevel for skill or infinite debuffs can happen
                abLevel = (ushort)Math.Max(template.AbilityLevel, (int)abLevel);
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
                abLevel = (ushort)source.Skill.Template.AbilityLevel;
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

    /// <summary>
    /// Запускает стартовые действия баффа (бонусы, пакет, специальные свойства).
    /// Делегирует <see cref="BuffStartDispelHandler"/> и <see cref="BuffSpecialHandler"/>.
    /// </summary>
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

    /// <summary>
    /// Применяет тиковый эффект (разовый или по области).
    /// Делегирует <see cref="BuffTickHandler"/>.
    /// </summary>
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

    /// <summary>
    /// Выполняет area-тик напрямую.
    /// </summary>
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

    /// <summary>
    /// Завершает бафф: откатывает бонусы, снимает специальные свойства.
    /// Делегирует <see cref="BuffStartDispelHandler"/> и <see cref="BuffSpecialHandler"/>.
    /// </summary>
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

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    public void WriteData(PacketStream stream, uint abLevel)
    {
        stream.WritePisc(0, GetDuration(abLevel) / 10, 0, (long)(Tick / 10)); // unk, Duration, unk / 10, Tick
    }

    public int GetDuration(uint abLevel)
    {
        return Math.Max(0, LevelDuration * (int)abLevel + Duration);
    }

    /// <summary>
    /// Возвращает интервал тика. Используется при инициализации <see cref="Buff"/>.
    /// </summary>
    public double GetTick()
    {
        return Tick;
    }
}

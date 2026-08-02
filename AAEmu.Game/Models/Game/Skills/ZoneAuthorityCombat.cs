using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// ZoneAuthority combat helpers.
/// Primary cast path is <c>Skill.Use</c> from <c>CSStartSkillPacket</c> (plot_only owns timing via plot;
/// non-plot uses ScheduleEffects ComputedDelay). This class keeps Tl helpers, plot start, and HP/Zone sync.
/// </summary>
public static class ZoneAuthorityCombat
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Allocate tl via SkillTlIdManager so PlotEnded ReleaseId matches.</summary>
    public static ushort NextTl(BaseUnit unit = null)
    {
        var id = SkillTlIdManager.GetNextId(unit);
        return id != 0 ? id : (ushort)1;
    }

    public static void StartPlot(Character character, uint skillId, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
    {
        var template = skill?.Template;
        if (template?.Plot == null)
            return;
        skill.TlId = tl;
        var plotTarget = ResolvePlotTarget(character, target);
        if (plotTarget == null)
        {
            Logger.Warn("ZoneAuthority plot skipped — no target skill={0}", skillId);
            return;
        }
        _ = System.Threading.Tasks.Task.Run(() => template.Plot.RunAsync(character, caster, plotTarget, target, skillObject, skill));
        Logger.Info("ZoneAuthority plot started skill={0} tl={1} plotOnly={2}", skillId, tl, template.PlotOnly);
    }

    private static BaseUnit ResolvePlotTarget(Character character, SkillCastTarget target)
    {
        var world = character.ParentWorld ?? WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (target is SkillCastUnitTarget ut && ut.ObjId != 0)
            return world?.GetBaseUnit(ut.ObjId);
        if (target is SkillCastDoodadTarget dt && dt.ObjId != 0)
            return world?.GetBaseUnit(dt.ObjId);
        return character;
    }

    /// <summary>Call from plot DamageEffect when ZoneAuthority mirrors HP to Zone.</summary>
    public static void SyncUnitPoints(uint objId, int hp, int mp) =>
        WorldIntegration.RelayUnitPointsToZone?.Invoke(objId, hp, mp);

    /// <summary>
    /// Zone NPC skill hit (ZWStartSkill). Dedicate applies Zone-local HP but has no ZWUnitDamaged —
    /// World must author SC UnitDamaged/Points for the client (and mirror player HP via WZUnitPoints).
    /// </summary>
    public static void ApplyNpcSkillHit(uint casterId, uint skillId, SkillCastTarget target, Skill skill)
    {
        if (target is not SkillCastUnitTarget unitTarget || unitTarget.ObjId == 0)
            return;

        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        var casterUnit = world?.GetUnit(casterId) as Unit ?? world?.GetNpc(casterId);
        var trg = world?.GetUnit(unitTarget.ObjId) as Unit;
        if (casterUnit == null || trg == null || trg.Hp <= 0)
            return;
        // NPC AI → player only. NPC↔NPC damage stays Zone-local (no ZW damaged).
        if (trg is not Character character)
            return;

        var template = skill?.Template;
        var damage = EstimateDamage(template, casterUnit, trg);
        if (damage <= 0)
        {
            // Non-damaging NPC cast (summon, buff, plot). Forcing a minimum here is what made
            // neutral NPCs appear to attack on sight.
            Logger.Debug(
                "ZoneAuthority NPC skill {0} from {1} carries no damage — not applying a hit",
                skillId, casterId);
            return;
        }

        var absorbed = 0;
        var tl = skill?.TlId ?? (ushort)0;
        var castAction = new CastSkill(skillId, tl);
        var casterObj = new SkillCasterUnit(casterId);

        trg.ReduceCurrentHp(casterUnit, damage);
        casterUnit.IsInBattle = true;
        character.IsInBattle = true;
        character.LastCombatActivity = DateTime.UtcNow;
        casterUnit.LastCombatActivity = DateTime.UtcNow;

        // Character.BroadcastPacket so hit floaters reach the client.
        character.BroadcastPacket(new SCUnitDamagedPacket(castAction, casterObj, casterId, trg.ObjId, damage, absorbed)
        {
            HitType = SkillHitType.MeleeHit
        }, true);
        character.BroadcastPacket(new SCUnitPointsPacket(trg.ObjId, trg.Hp, character.Mp), true);
        character.BroadcastPacket(new SCCombatEngagedPacket(casterId, character.ObjId), true);

        WorldIntegration.RelayUnitPointsToZone?.Invoke(trg.ObjId, trg.Hp, character.Mp);
        Logger.Info("ZoneAuthority NPC hit skill={0} caster={1} target={2} dmg={3} hp={4}/{5}",
            skillId, casterId, trg.ObjId, damage, trg.Hp, character.MaxHp);
    }

    /// <summary>
    /// Zone leash/reset skill (11503 <c>NPC 회귀</c>). Zone already decided to reset — restore World
    /// mirror HP/MP (skipping this left deer stuck at 1% after leash).
    /// </summary>
    /// <remarks>
    /// The skill must be <em>nothing but</em> heal and mana restore. Accepting any skill that merely
    /// contains a HealEffect made every NPC whose rotation includes one immortal: the Kraken (npc
    /// 7607) casts 14079 <c>크라켄의 먹구름</c> — InteractionEffect + HealEffect — every five
    /// seconds, so World reset it to full faster than a player could damage it and its HP sat at
    /// 5070228 through an entire fight. 11503 is pure heal+mana and still matches; only 229 of the
    /// 28240 skills carrying effects do.
    /// </remarks>
    public static void ApplyNpcSelfHealFromZoneSkill(uint casterId, Skill skill)
    {
        var template = skill?.Template;
        if (!IsLeashResetSkill(template, out var hasHeal, out var hasMana))
            return;

        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        var npc = world?.GetNpc(casterId);
        if (npc == null || npc.MaxHp <= 0)
            return;

        var beforeHp = npc.Hp;
        var beforeMp = npc.Mp;
        if (hasHeal)
            npc.Hp = npc.MaxHp;
        if (hasMana || npc.Mp <= 0)
            npc.Mp = Math.Max(npc.Mp, npc.MaxMp);
        npc.IsInBattle = false;
        WorldIntegration.BroadcastPacketToUnitViewers(
            new SCUnitPointsPacket(npc.ObjId, npc.Hp, npc.Mp), npc.ObjId);
        WorldIntegration.RelayUnitPointsToZone?.Invoke(npc.ObjId, npc.Hp, npc.Mp);
        Logger.Info("ZoneAuthority NPC self-heal skill={0} npc={1} hp {2}→{3} mp {4}→{5}",
            template.Id, casterId, beforeHp, npc.Hp, beforeMp, npc.Mp);
    }

    /// <summary>
    /// A Zone skill is the leash reset only when it does nothing but restore the caster: every
    /// effect is a <see cref="HealEffect"/> or <see cref="RestoreManaEffect"/>, and at least one is
    /// present. 11503 <c>NPC 회귀</c> qualifies; a combat skill that happens to carry a heal does
    /// not.
    /// </summary>
    public static bool IsLeashResetSkill(SkillTemplate template) =>
        IsLeashResetSkill(template, out _, out _);

    private static bool IsLeashResetSkill(SkillTemplate template, out bool hasHeal, out bool hasMana)
    {
        hasHeal = false;
        hasMana = false;
        if (template?.Effects == null || template.Effects.Count == 0)
            return false;

        foreach (var se in template.Effects)
        {
            switch (se.Template)
            {
                case HealEffect:
                    hasHeal = true;
                    break;
                case RestoreManaEffect:
                    hasMana = true;
                    break;
                default:
                    // Carries real content besides the restore — not a leash reset.
                    return false;
            }
        }

        return hasHeal || hasMana;
    }

    /// <summary>
    /// Force-heal a mirror NPC on leash (ZWClearCombat / ZWAggroRemove on the NPC itself).
    /// Do NOT heal when ClearCombat targets the player — Zone emits that while fights are still
    /// active; healing every in-battle NPC (and pushing WZUnitPoints to full) caused mid-fight resets.
    /// </summary>
    public static bool ResetMirrorNpcHp(uint objId)
    {
        var world = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (world == null)
            return false;

        var npc = world.GetNpc(objId);
        if (npc == null)
        {
            // Player ClearCombat (common) — relay SC only; NPC HP restore waits for 11503 / NPC ClearCombat.
            if (world.GetCharacterByObjId(objId) != null)
                Logger.Debug("ZWClearCombat on player {0} — skip mirror NPC heal", objId);
            return false;
        }

        if (npc.MaxHp <= 0)
            return false;

        var beforeHp = npc.Hp;
        var beforeMp = npc.Mp;
        npc.Hp = npc.MaxHp;
        npc.Mp = Math.Max(npc.MaxMp, 0);
        npc.SetBattleStateFromZone(false);
        if (WorldIntegration.IsStreamedUnitForAnyClient(objId) || beforeHp < npc.MaxHp || beforeMp != npc.Mp)
        {
            WorldIntegration.BroadcastPacketToUnitViewers(
                new SCUnitPointsPacket(objId, npc.Hp, npc.Mp), objId);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(objId, npc.Hp, npc.Mp);
        }
        Logger.Info("ZoneAuthority leash heal npc={0} hp {1}→{2} mp {3}→{4}",
            objId, beforeHp, npc.Hp, beforeMp, npc.Mp);
        return true;
    }

    private static int EstimateDamage(SkillTemplate template, Unit caster, Unit target)
    {
        if (template?.Effects == null)
            return FallbackNpcMeleeDamage(caster);

        // Mirror DamageEffect.Apply flags — do NOT treat LevelMd/FixedMax alone as triggers.
        // Skill 2 ships a second DamageEffect (6060) with fixed 30k–50k that must never be used here.
        var carriesDamage = false;
        foreach (var se in template.Effects)
        {
            if (se.Template is not DamageEffect de)
                continue;
            carriesDamage = true;

            // Weighted / conditional extras (weight 0 or absurd fixed) — skip unless primary weapon/level hit.
            if (se.Weight == 0 && !de.UseMainhandWeapon && !de.UseOffhandWeapon && !de.UseRangedWeapon && !de.UseLevelDamage)
                continue;
            if (de.UseFixedDamage && de.FixedMax >= 10_000)
                continue; // GM / siege-style outliers on shared auto-attack skill

            if (de.UseFixedDamage)
            {
                var fixedMin = Math.Max(0, de.FixedMin);
                var fixedMax = Math.Max(fixedMin, de.FixedMax);
                if (fixedMax <= 0)
                    continue;
                var rolled = Random.Shared.Next(fixedMin, fixedMax + 1);
                if (de.Multiplier > 0)
                    rolled = Math.Max(1, (int)(rolled * de.Multiplier));
                return Math.Max(1, rolled);
            }

            // Same milli-DPS scale as DamageEffect.Apply (Npc.Dps is raw*1000).
            float minDmg = 0f, maxDmg = 0f;
            if (de.UseLevelDamage)
            {
                var lvlMd = caster.LevelDps * Math.Max(de.LevelMd, 0f);
                minDmg += lvlMd * 0.5f;
                maxDmg += lvlMd * 1.5f;
            }

            var dpsInc = de.DamageType switch
            {
                DamageType.Melee => caster.DpsInc,
                DamageType.Magic => caster.MDps + caster.MDpsInc,
                DamageType.Ranged => caster.RangedDpsInc,
                _ => caster.DpsInc
            };
            maxDmg += dpsInc * 0.001f * Math.Max(de.DpsIncMultiplier, 0f);

            float weaponDamage = 0f;
            if (de.UseMainhandWeapon)
                weaponDamage += caster.Dps * 0.001f;
            if (de.UseOffhandWeapon)
                weaponDamage += caster.OffhandDps * 0.001f;
            if (de.UseRangedWeapon)
                weaponDamage += caster.RangedDps * 0.001f;

            var dpsMul = de.DpsMultiplier > 0 ? de.DpsMultiplier : 1f;
            maxDmg = dpsMul * weaponDamage + maxDmg;

            // Auto-attack cast bonus ~1.0s → variableDamage ≈ max
            var variableDamage = maxDmg;
            minDmg += variableDamage * 0.85f;
            maxDmg = variableDamage * 1.15f;
            if (de.Multiplier > 0)
            {
                minDmg *= de.Multiplier;
                maxDmg *= de.Multiplier;
            }

            if (maxDmg < 1f && minDmg < 1f)
                continue;

            var lo = Math.Max(1, (int)Math.Floor(minDmg));
            var hi = Math.Max(lo, (int)Math.Ceiling(maxDmg));
            return Random.Shared.Next(lo, hi + 1);
        }

        // A skill carrying no DamageEffect at all is not an attack — summons, plot drivers and
        // buffs all reach here. Falling back to melee turned every such cast into a hit: skill
        // 20835 (Summon Caretaker's Telescope, plot 688, zero effects) killed players outright
        // because the fallback's level cap resolves to caster.Level * 12.
        // The fallback still covers a real attack whose damage effects failed to resolve.
        if (!carriesDamage)
            return 0;

        return FallbackNpcMeleeDamage(caster);
    }

    /// <summary>Safe chip damage when template effects don't resolve (mirrors often lack weapons).</summary>
    private static int FallbackNpcMeleeDamage(Unit caster)
    {
        // Npc.Dps is milli-scaled (see DamageEffect: Dps * 0.001f). Never divide raw by 25.
        var weapon = (caster.Dps + caster.DpsInc) * 0.001f;
        var fromWeapon = (int)Math.Round(Math.Max(0f, weapon));
        var fromLevel = caster.Level * 3 + 4;
        var fromLevelDps = (int)Math.Round(Math.Max(0f, caster.LevelDps));
        var dmg = Math.Max(1, Math.Max(fromWeapon, Math.Max(fromLevel, fromLevelDps)));
        // Soft cap: lvl2 deer ~20–30, not 1200 one-shots.
        var cap = Math.Max(20, caster.Level * 12);
        return Math.Min(dmg, cap);
    }
}

/// <summary>Delayed fire for Zone NPC cast-time skills (ZWStartSkill → SC Fired + player damage).</summary>
public sealed class ZoneNpcSkillFireTask(
    uint skillId,
    ushort tl,
    uint casterId,
    SkillCastTarget target,
    Skill skill,
    SkillObject skillObject,
    uint fireAnimId = 0,
    int effectDelayMs = 0) : AAEmu.Game.Models.Tasks.Task
{
    public override void Execute()
    {
        var caster = new SkillCasterUnit(casterId);
        var fired = new SCSkillFiredPacket(skillId, tl, caster, target, skill, skillObject ?? new SkillObject())
        {
            EffectDelayMs = effectDelayMs
        };
        if (fireAnimId > 0)
            fired.FireAnimId = fireAnimId;
        WorldIntegration.BroadcastPacketToUnitViewers(fired, casterId);
        ZoneAuthorityCombat.ApplyNpcSkillHit(casterId, skillId, target, skill);
        ZoneAuthorityCombat.ApplyNpcSelfHealFromZoneSkill(casterId, skill);
    }
}

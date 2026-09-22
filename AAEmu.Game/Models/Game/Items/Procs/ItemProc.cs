using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>
/// Instance of ItemProcTemplate. Keeps track of cooldown, "owner" item
/// </summary>
public class ItemProc(uint templateId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// The <c>unit_reqs.owner_type</c> of a proc row (19 rows on 15 procs), the literal the client looks the rows
    /// up by while loading item_procs (x2game-dev.dll 0x39b14b20, string at 0x3a15b378).
    /// </summary>
    public const string UnitReqsOwnerType = "ItemProc";

    /// <summary>Proc template ids already reported as unusable; keeps the warning to one per id.</summary>
    private static readonly ConcurrentDictionary<uint, byte> WarnedMissing = new();

    /// <summary>
    /// Set while the proc skill is being cast. Its fire edge and its hits run before the cooldown is armed, and
    /// must not come back into this proc.
    /// </summary>
    private bool _inFlight;

    public uint TemplateId { get; set; } = templateId;
    public ItemProcTemplate Template { get; set; } = ItemManager.Instance.GetItemProcTemplate(templateId);
    public DateTime LastProc { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Rolls this proc for one event and casts its skill when every gate holds, in the order the rules class lays
    /// them out: trigger, finisher, cooldown, chance, unit_reqs, target. Returns true only when the skill was
    /// cast, which is also the only outcome that starts the cooldown.
    /// </summary>
    /// <param name="owner">The unit wearing the item.</param>
    /// <param name="other">The unit on the other side of the event, or null when there is none.</param>
    /// <param name="sourceSkillId">The skill behind the event, for trigger_skill_id and trigger_tag_id.</param>
    /// <param name="victimDied">Whether the hit killed its victim, for the finisher row.</param>
    /// <param name="ignoreRoll">Forces the proc through the chance roll and nothing else.</param>
    public bool Apply(Unit owner, Unit other = null, uint sourceSkillId = 0, bool victimDied = false, bool ignoreRoll = false)
    {
        // A proc row whose template or skill is missing used to reach Skill.Use with a null Template and
        // NRE on Template.Id. That threw inside DamageEffect's TakeDamageAny roll, which runs inside
        // PlotEventEffect's per-target loop — so one bad proc aborted every remaining target of that plot
        // effect, and an AoE that should hit five stopped after two.
        if (Template?.SkillTemplate == null)
        {
            if (WarnedMissing.TryAdd(TemplateId, 0))
                Logger.Warn("ItemProc {0} has no {1}; proc skipped",
                    TemplateId, Template == null ? "template" : "skill template");
            return false;
        }

        if (_inFlight)
            return false;

        var hasTriggerTag = Template.TriggerTagId != 0 &&
                            SkillManager.Instance.GetSkillTags(sourceSkillId).Contains(Template.TriggerTagId);
        if (!ItemProcRules.TriggerMatches(Template.TriggerSkillId, Template.TriggerTagId, sourceSkillId, hasTriggerTag))
            return false;

        if (!ItemProcRules.FinisherAllows(Template.Finisher, victimDied))
            return false;

        if (ItemProcRules.CooldownBlocks(LastProc, Template.CooldownSec, DateTime.UtcNow))
            return false;

        if (!ignoreRoll && !ItemProcRules.RollPasses(Template.ChanceRate, Random.Shared.Next(0, 100)))
            return false;

        // The 19 unit_reqs rows with owner_type "ItemProc": a buff or buff tag on the wearer (kinds 15, 30 and 98)
        // and a health band (kind 26). The band reads the requirement target's Hpp, and on a take-damage proc
        // that is the wearer - procs 87, 114 and 153 (a 20 %, 15 % and 50 % band) all use a take_damage_any
        // trigger with a self-target skill, so their names mean the wearer's health. The hit and heal kinds keep
        // the other side: procs 173 and 198 are hit_heal rows whose band reads the health of the healed unit.
        var requirementTarget = ItemProcRules.RequirementTargetIsOwner(Template.ChanceKind) ? owner : other ?? owner;
        if (!UnitRequirementsGameData.Instance.MeetOwnerRequirements(
                UnitReqsOwnerType, TemplateId, Template.OrUnitReqs, owner, requirementTarget))
            return false;

        var target = ItemProcRules.TargetSide(Template.SkillTemplate.TargetType) switch
        {
            ItemProcTargetSide.Owner => owner,
            ItemProcTargetSide.Other => other,
            _ => null
        };
        if (target == null)
        {
            Logger.Debug("ItemProc {0}: no target for skill {1} (target type {2})",
                TemplateId, Template.SkillId, Template.SkillTemplate.TargetType);
            return false;
        }

        // The cast used to go out as a Doodad-typed target carrying the wearer's own ObjId. Skill.GetInitialTarget
        // resolves that to the wearer, so a hostile proc skill failed CanAttack against its own caster and came
        // back NoTarget: none of the 40 rows whose skill targets another unit ever fired.
        var caster = SkillCaster.GetByType(SkillCasterType.Unit);
        caster.ObjId = owner.ObjId;
        var castTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
        castTarget.ObjId = target.ObjId;

        var skill = new Skill(Template.SkillTemplate) { FromItemProc = true };
        SkillResult result;
        _inFlight = true;
        try
        {
            // bypassGcd: the triggering skill wrote the owner's SkillLastUsed a moment ago in this same call
            // stack, so the 150 ms anti-spam gate would turn every proc on an instant trigger into
            // CooldownTime, and a proc that did fire would swallow the player's next press. The proc's own
            // cooldown_sec is the gate.
            result = skill.Use(owner, caster, castTarget, null, true, out _);
        }
        finally
        {
            _inFlight = false;
        }

        if (!ItemProcRules.StartsCooldown(result))
        {
            Logger.Debug("ItemProc {0}: skill {1} on {2} not cast ({3}); cooldown not started",
                TemplateId, Template.SkillId, target.ObjId, result);
            return false;
        }

        LastProc = DateTime.UtcNow;
        return true;
    }
}

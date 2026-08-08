using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>
/// Instance of ItemProcTemplate. Keeps track of cooldown, "owner" item
/// </summary>
public class ItemProc(uint templateId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Proc template ids already reported as unusable; keeps the warning to one per id.</summary>
    private static readonly ConcurrentDictionary<uint, byte> WarnedMissing = new();

    public uint TemplateId { get; set; } = templateId;
    public ItemProcTemplate Template { get; set; } = ItemManager.Instance.GetItemProcTemplate(templateId);
    public DateTime LastProc { get; set; } = DateTime.MinValue;

    public bool Apply(Unit owner, bool ignoreRoll = false)
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

        if (DateTime.UtcNow < LastProc.AddSeconds(Template.CooldownSec))
            return false;

        // ignoreRoll is meant to force the proc through, not to block it. And chance_rate is a plain
        // percentage (0-100 across every row of item_procs), so the roll has to fail on >= rate: with ">"
        // a 15% proc fired 16 times in 100, and the rows that carry rate 0 still fired on a roll of 0.
        if (!ignoreRoll && Random.Shared.Next(0, 100) >= Template.ChanceRate)
            return false;

        var caster = SkillCaster.GetByType(SkillCasterType.Unit);
        caster.ObjId = owner.ObjId;

        var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
        target.ObjId = owner.ObjId;

        var skill = new Skill(Template.SkillTemplate);
        skill.Use(owner, caster, target, null, false, out _);
        return true;
    }
}

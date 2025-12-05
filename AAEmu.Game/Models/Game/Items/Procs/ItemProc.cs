using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>
/// Instance of ItemProcTemplate. Keeps track of cooldown, "owner" item
/// </summary>
public class ItemProc(uint templateId)
{
    public uint TemplateId { get; set; } = templateId;
    public ItemProcTemplate Template { get; set; } = ItemManager.Instance.GetItemProcTemplate(templateId);
    public DateTime LastProc { get; set; } = DateTime.MinValue;

    public bool Apply(Unit owner, bool ignoreRoll = false)
    {
        if (DateTime.UtcNow < LastProc.AddSeconds(Template.CooldownSec))
            return false;

        if (ignoreRoll || Random.Shared.Next(0, 100) > Template.ChanceRate)
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

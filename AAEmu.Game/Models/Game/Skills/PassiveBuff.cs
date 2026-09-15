using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public class PassiveBuff
{
    public uint Id { get; set; }
    public PassiveBuffTemplate Template { get; set; }

    public PassiveBuff()
    {
    }

    public PassiveBuff(PassiveBuffTemplate template)
    {
        Id = template.Id;
        Template = template;
    }

    public void Apply(Unit owner)
    {
        // A passive whose template is missing is inert rather than a crash: passive_buffs 51, 268, 274 and
        // 289 ship without a row, their named buff may be missing too, and callers reach this from saved
        // characters and from npc/slave passive lists built straight from content.
        if (Template == null)
            return;

        // owner.Modifiers.AddModifiers(Template.BuffId);
        var template = SkillManager.Instance.GetBuffTemplate(Template.BuffId);
        if (template == null)
            return;

        var newEffect =
            new Buff(owner, owner, new SkillCasterUnit(owner.ObjId), template, null, DateTime.UtcNow)
            {
                Passive = true
            };

        owner.Buffs.AddBuff(newEffect);
    }

    public void Remove(Unit owner)
    {
        if (Template == null)
            return;

        // owner.Modifiers.RemoveModifiers(Template.BuffId);
        owner.Buffs.RemoveBuff(Template.BuffId);
    }
}

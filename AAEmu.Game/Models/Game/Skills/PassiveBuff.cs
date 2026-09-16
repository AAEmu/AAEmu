using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
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
                Passive = true,
                AbLevel = ResolveAbLevel(owner)
            };

        owner.Buffs.AddBuff(newEffect);
    }

    /// <summary>
    /// The ability level the passive's bonuses are scaled by (see
    /// <see cref="PassiveBuffLevelRules.AbLevelFor"/>). A character reads its own ability tree — and, for
    /// a general passive, its level, which is what <c>Character.GetAbLevel(General)</c> returns without
    /// touching <c>Abilities</c>. Every other owner (an NPC's or a slave's passive list) keeps the 1 the
    /// buff had before, so their numbers do not move.
    /// </summary>
    private uint ResolveAbLevel(Unit owner)
    {
        if (owner is not Character character)
            return 1u;

        if (Template.AbilityId == AbilityType.General)
            return PassiveBuffLevelRules.AbLevelFor(character.Level);

        // Abilities is built by Character.Load; a passive applied before that cannot be scaled.
        return character.Abilities == null
            ? 1u
            : PassiveBuffLevelRules.AbLevelFor(character.GetAbLevel(Template.AbilityId));
    }

    public void Remove(Unit owner)
    {
        if (Template == null)
            return;

        // owner.Modifiers.RemoveModifiers(Template.BuffId);
        owner.Buffs.RemoveBuff(Template.BuffId);
    }
}

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Cooldown : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Cooldown;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int cooldownTime,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Unit unit || skill?.Template == null)
            return;

        var modified = (uint)Math.Max(0, Math.Round(caster.ApplySkillModifiers(skill, SkillAttribute.Cooldown, cooldownTime)));
        var cooldownDuration = SkillCooldownDurationRules.ResolveDuration(modified, unit.GlobalCooldownMul);
        unit.Cooldowns.AddCooldown(skill.Template.Id, cooldownDuration, skill.Template.CooldownTags);

        // No mid-combat "set cooldown" SC — push the full snapshot so the HUD catches up.
        if (unit is Character character)
            character.SendPacket(new SCCooldownsPacket(character.Cooldowns));
    }
}

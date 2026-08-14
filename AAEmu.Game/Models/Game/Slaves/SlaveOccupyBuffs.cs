using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Helm occupy skills apply BuffEffects (Captain's Intuition / helm protection) on the driver.
/// ZoneAuthority CSStartSkill does not reliably land those on the PC, so BindSlave reapplies
/// BuffEffect rows only (not InteractionEffect — bind already happened).
/// </summary>
public static class SlaveOccupyBuffs
{
    public static IEnumerable<uint> BuffIdsFromSkill(SkillTemplate template)
    {
        if (template?.Effects == null)
            yield break;
        foreach (var effect in template.Effects)
        {
            if (effect?.Template is BuffEffect buffEffect && buffEffect.Buff != null)
                yield return buffEffect.Buff.Id;
        }
    }

    public static uint ResolveOccupySkillId(uint packetSkillId, Slave slave)
    {
        if (HasBuffEffects(packetSkillId))
            return packetSkillId;

        if (slave?.AttachedDoodads == null)
            return packetSkillId;

        foreach (var doodad in slave.AttachedDoodads)
        {
            if (doodad == null)
                continue;
            foreach (var func in DoodadManager.Instance.GetFuncsForGroup(doodad.FuncGroupId))
            {
                if (func.FuncType != "DoodadFuncAttachment" || func.SkillId == 0)
                    continue;
                if (HasBuffEffects(func.SkillId))
                    return func.SkillId;
            }
        }

        return packetSkillId;
    }

    public static void ApplyBuffEffects(Character character, uint occupySkillId, Slave slave = null)
    {
        if (character == null)
            return;

        var skillId = ResolveOccupySkillId(occupySkillId, slave);
        if (skillId == 0)
            return;

        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return;

        var caster = SkillCaster.GetByType(SkillCasterType.Unit);
        caster.ObjId = character.ObjId;
        var target = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
        target.ObjId = character.ObjId;
        var skill = new Skill(template) { SuppressZoneSkillRelay = true };

        foreach (var effect in template.Effects)
        {
            if (effect?.Template is not BuffEffect)
                continue;
            effect.Template.Apply(
                character,
                caster,
                character,
                target,
                new CastSkill(template.Id, 0),
                new EffectSource(skill),
                new SkillObject(),
                DateTime.UtcNow);
        }
    }

    private static bool HasBuffEffects(uint skillId)
    {
        if (skillId == 0)
            return false;
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        return BuffIdsFromSkill(template).Any();
    }
}

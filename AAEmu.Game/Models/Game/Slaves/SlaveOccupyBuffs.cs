using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
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

    /// <summary>
    /// Occupy skill must be a Driver-seat helm attachment on this hull. A client BindSlave
    /// skillType is only used when it matches that list; otherwise the first Driver occupy skill
    /// is used. Unknown/empty list → 0 (no buffs).
    /// </summary>
    public static uint ResolveOccupySkillId(uint packetSkillId, IReadOnlyList<uint> allowedOccupySkillIds)
    {
        if (allowedOccupySkillIds == null || allowedOccupySkillIds.Count == 0)
            return 0;

        if (packetSkillId != 0)
        {
            for (var i = 0; i < allowedOccupySkillIds.Count; i++)
            {
                if (allowedOccupySkillIds[i] == packetSkillId)
                    return packetSkillId;
            }
        }

        return allowedOccupySkillIds[0];
    }

    public static uint ResolveOccupySkillId(uint packetSkillId, Slave slave)
        => ResolveOccupySkillId(packetSkillId, HelmOccupySkillIds(slave).ToList());

    /// <summary>
    /// CSBindSlave always seats Driver. Only doodad_func_attachments whose seat is Driver
    /// may contribute occupy buffs (helm wheels); cannon/passenger/mast attachments must not.
    /// </summary>
    public static bool IsDriverOccupySeat(AttachPointKind attachPoint)
        => attachPoint == AttachPointKind.Driver;

    public static IEnumerable<uint> HelmOccupySkillIds(Slave slave)
    {
        if (slave?.AttachedDoodads == null)
            yield break;

        foreach (var doodad in slave.AttachedDoodads)
        {
            if (doodad == null)
                continue;
            foreach (var func in DoodadManager.Instance.GetFuncsForGroup(doodad.FuncGroupId))
            {
                if (func.FuncType != "DoodadFuncAttachment" || func.SkillId == 0)
                    continue;
                if (DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) is not DoodadFuncAttachment attach)
                    continue;
                if (!IsDriverOccupySeat(attach.AttachPointId))
                    continue;
                if (HasBuffEffects(func.SkillId))
                    yield return func.SkillId;
            }
        }
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

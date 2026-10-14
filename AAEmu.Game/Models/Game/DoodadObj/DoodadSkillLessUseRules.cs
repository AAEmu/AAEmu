using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// F-use often arrives with skill id 0. Loot funcs already run on that path; UI-open
/// funcs whose compact row has no skill must run there too, or the client opens the
/// window locally and the server never sends the data that window reads.
/// </summary>
public static class DoodadSkillLessUseRules
{
    public static bool RunsOnSkillLessUse(string funcType)
    {
        return funcType is "DoodadFuncLootItem"
            or "DoodadFuncLootPack"
            or "DoodadFuncCutdowning"
            or nameof(DoodadFuncCraftOrderBoardUiOpen)
            or nameof(DoodadFuncExpeditionPortalUiOpen);
    }

    /// <summary>
    /// Some doodad funcs name a <c>func_skill_id</c> whose skill row has zero
    /// <c>skill_effects</c> (guild worldgate list open). The client still casts that skill at the
    /// doodad; without a bridge the cast applies nothing and never calls <see cref="Doodad.Use"/>.
    /// </summary>
    public static bool ShouldDriveEffectlessSkillUse(uint skillId, int effectCount, uint funcSkillId) =>
        skillId != 0 && effectCount == 0 && funcSkillId == skillId;

    public static void DriveEffectlessDoodadUses(BaseUnit caster, uint skillId, int effectCount,
        IEnumerable<BaseUnit> targets)
    {
        if (caster == null || skillId == 0 || effectCount != 0 || targets == null)
            return;

        foreach (var target in targets)
        {
            if (target is not Doodad doodad)
                continue;
            var func = DoodadManager.Instance.GetFunc(doodad.FuncGroupId, skillId);
            if (func == null || !ShouldDriveEffectlessSkillUse(skillId, effectCount, func.SkillId))
                continue;
            doodad.Use(caster, skillId);
        }
    }
}

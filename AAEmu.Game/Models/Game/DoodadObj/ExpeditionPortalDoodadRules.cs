using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Guild worldgate doodads are the ones whose phase funcs include
/// <see cref="DoodadFuncExpeditionPortalUiOpen"/>. Their active phase also arms a multi-day
/// <c>DoodadFuncGrowth</c>; that state must survive a World bounce.
/// </summary>
public static class ExpeditionPortalDoodadRules
{
    public static bool IsPortalUiOpenFunc(string funcType) =>
        funcType == nameof(DoodadFuncExpeditionPortalUiOpen);

    public static bool PhaseOpensPortalUi(IEnumerable<string> funcTypes) =>
        funcTypes != null && funcTypes.Any(IsPortalUiOpenFunc);

    /// <summary>
    /// True when any phase of this template opens the guild portal UI — the binding should be
    /// persisted even when <c>UsePersistentHouseDoodads</c> is off.
    /// </summary>
    public static bool TemplateHasPortalUi(DoodadTemplate template, Func<uint, IEnumerable<string>> funcTypesForGroup)
    {
        if (template?.FuncGroups == null || funcTypesForGroup == null)
            return false;
        foreach (var group in template.FuncGroups)
        {
            if (PhaseOpensPortalUi(funcTypesForGroup(group.Id)))
                return true;
        }

        return false;
    }
}
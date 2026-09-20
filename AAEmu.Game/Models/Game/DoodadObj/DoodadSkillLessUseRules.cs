using AAEmu.Game.Models.Game.DoodadObj.Funcs;

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
            or nameof(DoodadFuncCraftOrderBoardUiOpen);
    }
}

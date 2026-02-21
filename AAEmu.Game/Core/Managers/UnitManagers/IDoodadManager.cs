using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.World;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public interface IDoodadManager
{
    bool Exist(uint templateId);
    DoodadTemplate GetTemplate(uint id);
    void Load();
    Doodad Create(WorldInstance parentWorld, uint bcId, uint templateId, GameObject ownerObject = null, bool skipPhaseInitialization = false);
    DoodadFunc GetFunc(uint funcId);
    DoodadFunc GetFunc(uint funcGroupId, uint skillId);
    List<DoodadFunc> GetFuncsForGroup(uint funcGroupId);
    List<DoodadPhaseFunc> GetPhaseFunc(uint funcGroupId);
    DoodadFuncTemplate GetFuncTemplate(uint funcId, string funcType);
    DoodadPhaseFuncTemplate GetPhaseFuncTemplate(uint funcId, string funcType);
    List<DoodadFuncGroups> GetDoodadFuncGroups(uint doodadTemplateId);
    List<uint> GetDoodadFuncGroupsId(uint doodadTemplateId);
    List<DoodadFunc> GetDoodadFuncs(uint doodadFuncGroupId);
    List<DoodadPhaseFunc> GetDoodadPhaseFuncs(uint funcGroupId);
    List<uint> GetDoodadFuncConsumeChangerItemList(uint doodadFuncConsumeChangerId);
    void DeleteDoodadById(MySqlConnection connection, MySqlTransaction transaction, uint dbId);
    List<uint> GetTreasureChestTemplateIds();
}

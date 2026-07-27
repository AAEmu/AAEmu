using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.World;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public interface IDoodadManager : ILoadable
{
    bool Exist(uint templateId);
    DoodadTemplate GetTemplate(uint id);
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

    Doodad CreatePlayerDoodad(Character character, uint id, float x, float y, float z, float zRot, float scale, ulong itemId, FarmType farmType = FarmType.Invalid, uint itemTemplateId = 0, int customData = 0, bool ignoreHouses = false);
    void CloseCoffersOpenedBy(Character character);
}

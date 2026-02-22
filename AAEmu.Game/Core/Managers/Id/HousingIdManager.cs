using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class HousingIdManager() : IdManager("HousingIdManager", FirstId, LastId, ObjTables, Exclude), IHousingIdManager
{
    private static HousingIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "housings", "id" } };

    public static HousingIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<HousingIdManager>() ?? new HousingIdManager();
}

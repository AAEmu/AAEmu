using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class FamilyIdManager() : IdManager("FamilyIdManager", FirstId, LastId, ObjTables, Exclude, true), IFamilyIdManager
{
    private static FamilyIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "characters", "family" } };

    public static FamilyIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<FamilyIdManager>() ?? new FamilyIdManager();
}

using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class CrimeIdManager() : IdManager("CrimeIdManager", FirstId, LastId, ObjTables, Exclude), ICrimeIdManager
{
    private static CrimeIdManager _instance;
    private const uint FirstId = 0x00001000;
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "crime", "id" } };

    public static CrimeIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<CrimeIdManager>() ?? new CrimeIdManager();
}

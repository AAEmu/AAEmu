using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class WorldIdManager() : IdManager("WorldIdManager", FirstId, LastId, ObjTables, Exclude), IWorldIdManager
{
    private static WorldIdManager _instance;
    private const uint FirstId = 0x00000064;
    private const uint LastId = 0xFFFFFFFE;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static WorldIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<WorldIdManager>() ?? new WorldIdManager();
}

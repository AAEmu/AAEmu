using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class GimmickIdManager() : IdManager("GimmickIdManager", FirstId, LastId, ObjTables, Exclude), IGimmickIdManager
{
    private static GimmickIdManager _instance;
    private const uint FirstId = 0x0001;
    private const uint LastId = 0xFFFE;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static GimmickIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<GimmickIdManager>() ?? new GimmickIdManager();
}

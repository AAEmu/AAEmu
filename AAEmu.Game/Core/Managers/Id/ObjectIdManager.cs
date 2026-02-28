using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class ObjectIdManager() : IdManager("ObjectIdManager", FirstId, LastId, ObjTables, Exclude), IObjectIdManager
{
    private static ObjectIdManager _instance;
    private const uint FirstId = 0x00000100;
    private const uint LastId = 0x00FFFFFE;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static ObjectIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<ObjectIdManager>() ?? new ObjectIdManager();
}

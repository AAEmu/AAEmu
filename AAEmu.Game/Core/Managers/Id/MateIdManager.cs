using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class MateIdManager() : IdManager("MateIdManager", FirstId, LastId, ObjTables, Exclude), IMateIdManager
{
    private static MateIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "mates", "id" } };

    public static MateIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<MateIdManager>() ?? new MateIdManager();
}

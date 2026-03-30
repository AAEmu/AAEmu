using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class TrialIdManager() : IdManager("TrialIdManager", FirstId, LastId, ObjTables, Exclude), ITrialIdManager
{
    private static TrialIdManager _instance;
    private const uint FirstId = 0x00001000;
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static TrialIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<TrialIdManager>() ?? new TrialIdManager();
}

using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class ExpeditionIdManager() : IdManager("ExpeditionIdManager", FirstId, LastId, ObjTables, Exclude), IExpeditionIdManager
{
    private static ExpeditionIdManager _instance;
    private const uint FirstId = 1000; // Based on official packets
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "expeditions", "id" } };

    public static ExpeditionIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<ExpeditionIdManager>() ?? new ExpeditionIdManager();
}

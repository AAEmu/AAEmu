using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class TradeIdManager() : IdManager("TradeIdManager", FirstId, LastId, ObjTables, Exclude), ITradeIdManager
{
    private static TradeIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static TradeIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<TradeIdManager>() ?? new TradeIdManager();
}

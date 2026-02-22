using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class ShipyardIdManager() : IdManager("ShipyardIdManager", FirstId, LastId, ObjTables, Exclude), IShipyardIdManager
{
    private static ShipyardIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "shipyards", "id" } };

    public static ShipyardIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<ShipyardIdManager>() ?? new ShipyardIdManager();
}

using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class ContainerIdManager() : IdManager("ContainerIdManager", FirstId, LastId, ObjTables, Exclude), IContainerIdManager
{
    private static ContainerIdManager _instance;
    private const uint FirstId = 0x00010000; // random value
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "item_containers", "container_id" } };

    public static ContainerIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<ContainerIdManager>() ?? new ContainerIdManager();
}

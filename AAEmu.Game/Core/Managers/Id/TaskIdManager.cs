using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class TaskIdManager() : IdManager("TaskIdManager", FirstId, LastId, ObjTables, Exclude), ITaskIdManager
{
    private static TaskIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x7FFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static TaskIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<TaskIdManager>() ?? new TaskIdManager();
}
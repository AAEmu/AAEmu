using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class QuestIdManager() : IdManager("QuestIdManager", FirstId, LastId, ObjTables, Exclude), IQuestIdManager
{
    private static QuestIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "quests", "id" } };

    public static QuestIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<QuestIdManager>() ?? new QuestIdManager();
}

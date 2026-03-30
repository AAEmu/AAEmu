using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class ChatIdManager() : IdManager("ChatIdManager", FirstId, LastId, ObjTables, Exclude), IChatIdManager
{
    private static ChatIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x0000FFFE;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static ChatIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<ChatIdManager>() ?? new ChatIdManager();
}

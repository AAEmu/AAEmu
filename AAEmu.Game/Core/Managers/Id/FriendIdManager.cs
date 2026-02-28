using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class FriendIdManager() : IdManager("FriendIdManager", FirstId, LastId, ObjTables, Exclude), IFriendIdManager
{
    private static FriendIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "friends", "id" } };

    public static FriendIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<FriendIdManager>() ?? new FriendIdManager();
}

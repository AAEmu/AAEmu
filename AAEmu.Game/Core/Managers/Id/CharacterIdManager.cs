using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class CharacterIdManager() : IdManager("CharacterIdManager", FirstId, LastId, ObjTables, Exclude), ICharacterIdManager
{
    private static CharacterIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "characters", "id" }, { "slaves", "id" } };

    public static CharacterIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<CharacterIdManager>() ?? new CharacterIdManager();
}

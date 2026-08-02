using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

/// <inheritdoc cref="INonUnitObjectIdManager"/>
public class NonUnitObjectIdManager()
    : IdManager("NonUnitObjectIdManager", FirstId, LastId, ObjTables, Exclude), INonUnitObjectIdManager
{
    private static NonUnitObjectIdManager _instance;

    private const uint FirstId = ObjectIdManager.DedicateMaxUnitExclusive; // 101000
    private const uint LastId = 0x00FFFFFE;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static NonUnitObjectIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<NonUnitObjectIdManager>()
                       ?? new NonUnitObjectIdManager();
}

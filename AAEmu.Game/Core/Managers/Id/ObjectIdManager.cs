using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

/// <summary>
/// Broadcast ObjIds for units that index the dedicate unit table
/// (<c>max_unit</c>, default 101000). Characters, mates, slaves, houses,
/// transfers, portals, shipyards, and Zone NPC mirrors share this pool —
/// matching retail SC bc density under max_unit.
/// </summary>
public class ObjectIdManager() : IdManager("ObjectIdManager", FirstId, LastId, ObjTables, Exclude), IObjectIdManager
{
    private static ObjectIdManager _instance;

    /// <summary>Matches dedicate default <c>max unit</c> (world+test). Exclusive end.</summary>
    public const uint DedicateMaxUnitExclusive = 101000;

    /// <summary>Whether <paramref name="objId"/> is valid for a Zone unit field.</summary>
    public static bool IsZoneUnitId(uint objId) => objId is > 0 and <= LastId;

    private const uint FirstId = 0x00000100;
    private const uint LastId = DedicateMaxUnitExclusive - 1; // 100999
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static ObjectIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<ObjectIdManager>() ?? new ObjectIdManager();
}

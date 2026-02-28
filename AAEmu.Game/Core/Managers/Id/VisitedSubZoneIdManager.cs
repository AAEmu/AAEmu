using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Managers.Id;

public class VisitedSubZoneIdManager() : IdManager("VisitedSubZoneIdMananger", FirstId, LastId, ObjTables, Exclude), IVisitedSubZoneIdManager
{
    private static VisitedSubZoneIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "portal_visited_district", "id" } };

    public static VisitedSubZoneIdManager Instance =>
        _instance ??= SingletonContainer.ServiceProvider?.GetService<VisitedSubZoneIdManager>() ?? new VisitedSubZoneIdManager();
}

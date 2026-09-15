using AAEmu.Game.Models.Game.Expeditions.Activities;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionActivityRepository
{
    IReadOnlyList<ExpeditionPortalPoint> GetPortals(uint expeditionId);
    bool TryAddPortal(ExpeditionPortalPoint portal, int capacity);
    bool RenamePortal(uint expeditionId, uint portalId, string name);
    bool DeletePortal(uint expeditionId, uint portalId);

    IReadOnlyList<ExpeditionManagementHistory> GetManagementHistories(uint expeditionId, int limit);
    IReadOnlyList<ExpeditionShopHistory> GetShopHistories(uint expeditionId, int limit);
    IReadOnlyList<ExpeditionWarHistory> GetWarHistories(uint expeditionId, int limit);
    IReadOnlyList<ExpeditionInstanceHistory> GetInstanceHistories(uint expeditionId, int limit);
    void AddManagementHistory(uint expeditionId, ExpeditionManagementHistory history);
    void AddManagementHistory(uint expeditionId, ExpeditionManagementHistory history,
        MySqlConnection connection, MySqlTransaction transaction);
    void AddShopHistory(uint expeditionId, ExpeditionShopHistory history);
    void AddShopHistory(uint expeditionId, ExpeditionShopHistory history,
        MySqlConnection connection, MySqlTransaction transaction);
    void AddWarHistory(uint expeditionId, ExpeditionWarHistory history);
    void AddWarHistory(ExpeditionWarHistory history, MySqlConnection connection, MySqlTransaction transaction);
    void AddInstanceHistory(uint expeditionId, ExpeditionInstanceHistory history);
    void AddInstanceHistory(uint expeditionId, ExpeditionInstanceHistory history,
        MySqlConnection connection, MySqlTransaction transaction);

}

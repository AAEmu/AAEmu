using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Trading;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class ButlerSpecialtyTradeSettlement(
    SpecialtyManager specialtyManager,
    ISpecialtyMarketStore marketStore) : IButlerSpecialtyTradeSettlement
{
    public bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId,
        out SpecialtyMarketWrite market) =>
        specialtyManager.TryPrepareButlerTradeDelivery(npcId, productItemId, zoneGroupId, out market);

    public void Apply(SpecialtyMarketWrite market, MySqlConnection connection,
        MySqlTransaction transaction) =>
        marketStore.Apply(connection, transaction, market);

    public void Commit(SpecialtyMarketWrite market) => specialtyManager.CommitButlerTradeMarketWrite(market);
}

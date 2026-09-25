using AAEmu.Game.Models.Game.Trading;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IButlerSpecialtyTradeSettlement
{
    bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId, out SpecialtyMarketWrite market);

    void Apply(SpecialtyMarketWrite market, MySqlConnection connection, MySqlTransaction transaction);

    void Commit(SpecialtyMarketWrite market);
}

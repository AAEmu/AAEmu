using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Trading;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class ButlerSpecialtyTradeSettlement(
    SpecialtyManager specialtyManager,
    ISpecialtyMarketStore marketStore) : IButlerSpecialtyTradeSettlement
{
    public bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId, long freshnessElapsedSeconds,
        uint ownerId, string ownerName, DateTime settledAtUtc,
        out ButlerSpecialtyTradeDeliveryQuote quote, out BaseMail ownerPayoutMail) =>
        specialtyManager.TryPrepareButlerTradeDelivery(npcId, productItemId, zoneGroupId,
            freshnessElapsedSeconds, ownerId, ownerName, settledAtUtc, out quote, out ownerPayoutMail);

    public bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId,
        out SpecialtyMarketWrite market) =>
        specialtyManager.TryPrepareButlerTradeDelivery(npcId, productItemId, zoneGroupId, out market);

    public void Apply(ButlerSpecialtyTradeDeliveryQuote quote, MySqlConnection connection,
        MySqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(quote);
        marketStore.Apply(connection, transaction, quote.Market);
    }

    public void Commit(ButlerSpecialtyTradeDeliveryQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        specialtyManager.CommitButlerTradeMarketWrite(quote.Market);
    }
}

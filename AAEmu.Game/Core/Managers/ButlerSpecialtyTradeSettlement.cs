using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Trading;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The mail operations a farmhand specialty-trade settlement needs. Narrower than
/// <see cref="IMailManager"/> so the settlement path can be exercised without standing up the
/// whole mail graph.
/// </summary>
public interface IButlerSpecialtyTradePayoutPublisher
{
    bool TryPrepareBatch(IReadOnlyList<BaseMail> mails, out PreparedMailBatch batch);

    void PersistPreparedBatch(IReadOnlyList<BaseMail> mails, MySqlConnection connection, MySqlTransaction transaction);

    bool PublishPreparedBatch(PreparedMailBatch batch, bool alreadyPersisted = false);

    void CancelPreparedBatch(PreparedMailBatch batch);
}

public sealed class ButlerSpecialtyTradePayoutPublisher(IMailManager mailManager)
    : IButlerSpecialtyTradePayoutPublisher
{
    public bool TryPrepareBatch(IReadOnlyList<BaseMail> mails, out PreparedMailBatch batch) =>
        mailManager.TryPrepareBatch(mails, out batch);

    public void PersistPreparedBatch(IReadOnlyList<BaseMail> mails, MySqlConnection connection,
        MySqlTransaction transaction) =>
        mailManager.PersistPreparedBatch(mails, connection, transaction);

    public bool PublishPreparedBatch(PreparedMailBatch batch, bool alreadyPersisted = false) =>
        mailManager.PublishPreparedBatch(batch, alreadyPersisted);

    public void CancelPreparedBatch(PreparedMailBatch batch) =>
        mailManager.CancelPreparedBatch(batch);
}

public interface IButlerSpecialtyTradeSettlement
{
    /// <summary>
    /// Resolves the delivery's market write and the owner's payout in one step. The quote carries
    /// the route ratio the payout was computed from, so callers must never re-read it afterwards.
    /// </summary>
    bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId, long freshnessElapsedSeconds,
        uint ownerId, string ownerName, DateTime settledAtUtc,
        out ButlerSpecialtyTradeDeliveryQuote quote, out BaseMail ownerPayoutMail);

    bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId, out SpecialtyMarketWrite market);

    void Apply(ButlerSpecialtyTradeDeliveryQuote quote, MySqlConnection connection, MySqlTransaction transaction);

    void Commit(ButlerSpecialtyTradeDeliveryQuote quote);
}

using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Managers;

public readonly record struct ButlerSpecialtyTradePersistResult(
    bool Success,
    bool Ambiguous,
    long JobId = 0);

public interface IButlerSpecialtyTradePersistence
{
    ButlerSpecialtyTradePersistResult Register(
        CharacterButlerRecord proposed,
        ButlerSpecialtyTradeJobCandidate candidate,
        IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);

    ButlerSpecialtyTradePersistResult Cancel(uint characterId, long jobId);

    /// <summary>
    /// Deletes the job, applies the delivery's market write and persists the owner's payout letter
    /// in one transaction, so a delivery can never land without its money or the money land twice.
    /// </summary>
    ButlerSpecialtyTradePersistResult Settle(
        uint characterId,
        long jobId,
        ButlerSpecialtyTradeDeliveryQuote quote,
        BaseMail ownerPayoutMail);

    bool TryLoadSpecialtyTradeJob(uint characterId, long jobId, out ButlerSpecialtyTradeJob job);
}

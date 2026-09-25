using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Items;
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

    ButlerSpecialtyTradePersistResult Settle(
        uint characterId,
        long jobId,
        SpecialtyMarketWrite market);

    bool TryLoadSpecialtyTradeJob(uint characterId, long jobId, out ButlerSpecialtyTradeJob job);
}

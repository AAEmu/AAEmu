using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Managers;

public interface IFamilyPurchaseRepository
{
    void CommitItemConsumption(IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);

    /// <summary>
    /// Saves the family's pending roster change (removed members, rejoin deadlines, EXP loss, or the
    /// deletion of an empty family) and the certificate consumption in one transaction.
    /// </summary>
    void CommitDeparture(Family family, IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);

    bool TryCommitExpansion(uint familyId, uint expectedIncreaseCount, uint newIncreaseCount,
        IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);

    bool TryCommitRename(uint familyId, string expectedName, long expectedChangeNameTime,
        string newName, long newChangeNameTime, IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);
}

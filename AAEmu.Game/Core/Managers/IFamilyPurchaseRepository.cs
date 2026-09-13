using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Managers;

public interface IFamilyPurchaseRepository
{
    void CommitItemConsumption(IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);

    bool TryCommitExpansion(uint familyId, uint expectedIncreaseCount, uint newIncreaseCount,
        IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);

    bool TryCommitRename(uint familyId, string expectedName, long expectedChangeNameTime,
        string newName, long newChangeNameTime, IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots);
}

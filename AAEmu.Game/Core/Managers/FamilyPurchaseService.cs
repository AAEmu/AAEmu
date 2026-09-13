using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Families;
using NLog;

namespace AAEmu.Game.Core.Managers;

public enum FamilyPurchaseFailure
{
    None,
    InvalidRequest,
    MissingItems,
    ConcurrentChange,
    PersistenceFailed
}

public readonly record struct FamilyPurchaseResult(
    bool Success,
    FamilyPurchaseFailure Failure,
    Action DeferredPublication = null)
{
    public void PublishDeferred() => DeferredPublication?.Invoke();
}

public interface IFamilyPurchaseService
{
    FamilyPurchaseResult ConsumeInvitation(Character inviter);
    FamilyPurchaseResult Expand(Character owner, Family family, uint itemId, int itemCount);
    FamilyPurchaseResult Rename(Character owner, Family family, string newName, long changeNameTime);
}

public sealed class FamilyPurchaseService(
    IFamilyPurchaseRepository repository,
    IItemManager itemManager) : IFamilyPurchaseService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public FamilyPurchaseResult ConsumeInvitation(Character inviter) =>
        inviter == null
            ? Failed(FamilyPurchaseFailure.InvalidRequest)
            : Commit(inviter, FamilyContentConfig.JoinLeaveItem, 1,
            snapshots =>
            {
                repository.CommitItemConsumption(snapshots);
                return true;
            },
            () => { });

    public FamilyPurchaseResult Expand(Character owner, Family family, uint itemId, int itemCount)
    {
        if (owner == null || family == null || itemId == 0 || itemCount <= 0)
            return Failed(FamilyPurchaseFailure.InvalidRequest);

        var expected = family.IncreasedMemberCount;
        return Commit(owner, itemId, itemCount,
            snapshots => repository.TryCommitExpansion(family.Id, expected, checked(expected + 1), snapshots),
            () => family.IncreasedMemberCount = checked(expected + 1));
    }

    public FamilyPurchaseResult Rename(Character owner, Family family, string newName, long changeNameTime)
    {
        if (owner == null || family == null || string.IsNullOrEmpty(newName) || changeNameTime <= 0)
            return Failed(FamilyPurchaseFailure.InvalidRequest);

        var expectedName = family.Name;
        var expectedTime = family.ChangeNameTime;
        return Commit(owner, FamilyContentConfig.NameChangeItem, FamilyContentConfig.NameChangeItemCount,
            snapshots => repository.TryCommitRename(family.Id, expectedName, expectedTime,
                newName, changeNameTime, snapshots),
            () =>
            {
                family.Name = newName;
                family.ChangeNameTime = changeNameTime;
            });
    }

    private FamilyPurchaseResult Commit(Character owner, uint itemId, int itemCount,
        Func<IReadOnlyList<ItemPersistenceSnapshot>, bool> persist, Action applyFamily)
    {
        ItemConsumptionPublication publication = null;
        var result = Failed(FamilyPurchaseFailure.InvalidRequest);
        using (PersistenceOperationScope.Enter())
        {
            lock (owner.Inventory.MutationSyncRoot)
            {
                if (!owner.Inventory.TryPlanBagConsumption(itemId, itemCount, out var consumption))
                    return Failed(FamilyPurchaseFailure.MissingItems);

                var snapshots = consumption.CapturePersistenceSnapshots(itemManager);
                try
                {
                    if (!persist(snapshots))
                        return Failed(FamilyPurchaseFailure.ConcurrentChange);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to persist family purchase for family {0}", owner.Family);
                    return Failed(FamilyPurchaseFailure.PersistenceFailed);
                }

                publication = consumption.ApplyCommitted(ItemTaskType.SkillEffectConsumption);
                applyFamily();
                result = new FamilyPurchaseResult(true, FamilyPurchaseFailure.None);
                try { publication.PublishPackets(); }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to publish committed family purchase item packets for character {0}",
                        owner.Id);
                }
            }
        }
        return publication == null ? result : result with
        {
            DeferredPublication = () =>
            {
                try
                {
                    publication.PublishCallbacks();
                    owner.ItemUseByTemplate(itemId);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to publish committed family purchase callbacks for character {0}", owner.Id);
                }
            }
        };
    }

    private static FamilyPurchaseResult Failed(FamilyPurchaseFailure failure) => new(false, failure);
}

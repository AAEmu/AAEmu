using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Mails;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public readonly record struct ButlerUnbindServiceResult(
    bool Success,
    ErrorMessageType Error,
    uint PreviousHouseId);

public interface IButlerUnbindService
{
    ButlerUnbindServiceResult UnbindLocked(CharacterButler butler, uint expectedHouseId, Character owner);
}

/// <summary>
/// Returns farmhand-held items and clears residence-only work in one caller transaction. The caller
/// holds PersistenceGate and the farmhand operation lock; this service keeps state and inventory
/// guards through both the database commit and the matching live apply.
/// </summary>
public sealed class ButlerUnbindService(
    IButlerRepository repository,
    IMailManager mailManager,
    IItemManager itemManager,
    INameManager nameManager) : IButlerUnbindService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    internal Func<MySqlConnection> OpenConnection { get; init; } = MySQL.CreateConnection;

    public ButlerUnbindService() : this(
        new MySqlButlerRepository(),
        MailManager.Instance,
        ItemManager.Instance,
        NameManager.Instance)
    {
    }

    public ButlerUnbindServiceResult UnbindLocked(
        CharacterButler butler,
        uint expectedHouseId,
        Character owner)
    {
        ArgumentNullException.ThrowIfNull(butler);
        if (!PersistenceGate.IsOperationHeld || !Monitor.IsEntered(butler.OperationSyncRoot))
            throw new InvalidOperationException("Farmhand unbinding requires the persistence and operation guards.");

        lock (butler.SyncRoot)
        {
            if (butler.IsDeleted)
                return Failed(ErrorMessageType.InvalidTarget);

            var previousHouseId = butler.HouseId;
            if (previousHouseId == 0)
                return expectedHouseId == 0
                    ? Failed(ErrorMessageType.NoInteractionAvailable)
                    : Succeeded(0);
            if (expectedHouseId != 0 && previousHouseId != expectedHouseId)
                return Succeeded(0);

            var storedItems = butler.SnapshotStoredItems();
            var harvestJobs = butler.SnapshotHarvestJobs();
            var proposed = butler.Snapshot() with { HouseId = 0, RemainProductionCost = 0 };
            if (owner != null && owner.Id != butler.CharacterId)
                return Failed(ErrorMessageType.InvalidTarget);

            InventoryMutationLease inventoryLease = null;
            ExistingItemMailDeliveryPlan mailPlan = null;
            var committed = false;
            try
            {
                IReadOnlyList<Item> items = Array.Empty<Item>();
                if (storedItems.Count > 0)
                {
                    var systemContainer = itemManager.FindItemContainerFor(
                        butler.CharacterId, SlotType.System, 0);
                    if (systemContainer == null || systemContainer.OwnerId != butler.CharacterId ||
                        systemContainer.ContainerType != SlotType.System ||
                        (owner != null && !ReferenceEquals(owner.Inventory.SystemContainer, systemContainer)) ||
                        !systemContainer.TryAcquireFarmhandMutation(owner?.Inventory, out inventoryLease) ||
                        !TryResolveStoredItems(
                            butler.CharacterId, systemContainer, storedItems, out items))
                        return Failed(ErrorMessageType.InternalError);
                }

                if (items.Count > 0 && !TryCreateReturnMailPlan(butler.CharacterId, items, out mailPlan))
                    return Failed(ErrorMessageType.InternalError);

                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                if (!repository.TryChangeHouse(proposed, previousHouseId, connection, transaction))
                {
                    transaction.Rollback();
                    return Failed(ErrorMessageType.InternalError);
                }

                var removedJobs = repository.DeleteAllHarvestJobs(butler.CharacterId, connection, transaction);
                if (removedJobs != harvestJobs.Count)
                    throw new InvalidOperationException(
                        $"Farmhand unbind removed {removedJobs}/{harvestJobs.Count} harvest jobs for character {butler.CharacterId}.");

                var removedItems = repository.DeleteAllStoredItems(butler.CharacterId, connection, transaction);
                if (removedItems != storedItems.Count)
                    throw new InvalidOperationException(
                        $"Farmhand unbind removed {removedItems}/{storedItems.Count} stored-item rows for character {butler.CharacterId}.");

                if (mailPlan != null && !mailPlan.TryPersistOn(connection, transaction))
                    throw new InvalidOperationException(
                        $"Could not persist returned farmhand items for character {butler.CharacterId}.");

                transaction.Commit();
                committed = true;

                butler.Apply(proposed);
                butler.ClearHarvestJobs();
                butler.ClearStoredItems();
                mailPlan?.Commit();
                return Succeeded(previousHouseId);
            }
            catch (Exception ex) when (!committed)
            {
                Logger.Error(ex, "Failed to unbind farmhand for character {0} from house {1}",
                    butler.CharacterId, previousHouseId);
                return Failed(ErrorMessageType.InternalError);
            }
            finally
            {
                mailPlan?.Dispose();
                inventoryLease?.Dispose();
            }
        }
    }

    private bool TryResolveStoredItems(
        uint characterId,
        ItemContainer systemContainer,
        IReadOnlyList<ButlerStoredItem> storedItems,
        out IReadOnlyList<Item> items)
    {
        items = Array.Empty<Item>();
        if (storedItems.Count == 0)
            return true;

        var resolved = new List<Item>(storedItems.Count);
        var ids = new HashSet<ulong>();
        foreach (var stored in storedItems)
        {
            var item = itemManager.GetItemByItemId(stored.ItemId);
            if (!ids.Add(stored.ItemId) || item == null || item.Id != stored.ItemId ||
                item.OwnerId != characterId || item.Count <= 0 || item.SlotType != SlotType.System ||
                !ReferenceEquals(item._holdingContainer, systemContainer) ||
                !systemContainer.Items.Contains(item, ReferenceEqualityComparer.Instance))
                return false;
            resolved.Add(item);
        }

        items = resolved.AsReadOnly();
        return true;
    }

    private bool TryCreateReturnMailPlan(
        uint characterId,
        IReadOnlyList<Item> items,
        out ExistingItemMailDeliveryPlan plan)
    {
        var receiverName = nameManager.GetCharacterName(characterId);
        if (string.IsNullOrWhiteSpace(receiverName))
        {
            plan = null;
            return false;
        }

        var now = DateTime.UtcNow;
        return mailManager.TryCreateExistingItemDeliveryPlan(
            items,
            (_, _) => new BaseMail
            {
                MailType = MailType.SysExpress,
                ReceiverName = receiverName,
                Title = "Farmhand Items Returned",
                Header =
                {
                    SenderId = 0,
                    SenderName = "Farmhand",
                    ReceiverId = characterId,
                    Status = MailStatus.Unread
                },
                Body =
                {
                    Text = "Items stored by your farmhand were returned when it was unbound.",
                    SendDate = now,
                    RecvDate = now
                }
            },
            out plan);
    }

    private static ButlerUnbindServiceResult Failed(ErrorMessageType error) => new(false, error, 0);

    private static ButlerUnbindServiceResult Succeeded(uint previousHouseId) =>
        new(true, ErrorMessageType.NoErrorMessage, previousHouseId);
}

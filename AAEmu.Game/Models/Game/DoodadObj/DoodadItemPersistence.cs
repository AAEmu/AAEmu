using System.Data.Common;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj;

public static class DoodadItemPersistence
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    internal static bool TryInitializeAndPersistPlacement(
        Doodad doodad,
        Action initialize,
        Func<bool> persist,
        Action rollback,
        Action<uint> releaseObjectId)
    {
        // Lifecycle tasks use the same monitor, so none can act on this doodad before placement commits.
        lock (doodad)
        {
            doodad.IsPlacementPending = true;
            doodad.IsPersistent = true;
            var committed = false;
            try
            {
                initialize();
                if (!persist())
                    return false;

                committed = true;
                doodad.IsPlacementPending = false;
                return true;
            }
            finally
            {
                if (!committed)
                {
                    doodad.IsPersistent = false;
                    doodad.ItemId = 0;
                    doodad.ItemTemplateId = 0;
                    try
                    {
                        doodad.FuncTask?.Cancel();
                    }
                    finally
                    {
                        doodad.FuncTask = null;
                        try
                        {
                            rollback();
                        }
                        finally
                        {
                            releaseObjectId(doodad.ObjId);
                        }
                    }
                }
            }
        }
    }

    public static bool TrySavePlacement(Item item, Doodad doodad)
    {
        using var persistence = MailManager.Instance.DeferPersist();
        var owner = item._holdingContainer?.Owner as Character;
        lock (owner?.StateSyncRoot ?? item)
        {
            return TryCommit(connection =>
            {
                var inventory = owner == null ? null : ItemManager.Instance.CaptureInventory(owner.Id);
                SavePlacement(connection.Connection, connection.Transaction, item, inventory,
                    () => doodad.Save(connection.Connection, connection.Transaction));
            });
        }
    }

    internal static void SavePlacement(DbConnection connection, DbTransaction transaction, Item item,
        InventoryPersistenceSnapshot inventory, Action saveDoodad)
    {
        // A freshly produced pack and its ingredient decrements/deletions cross the crash
        // boundary together. Never commit the ground product ahead of its source inventory.
        inventory?.Apply(connection, transaction);
        ItemPersistence.Save(connection, transaction, item);
        saveDoodad();
    }

    public static bool TrySaveRecovery(Item item, Doodad doodad) =>
        TryCommit(connection =>
        {
            ItemPersistence.Save(connection.Connection, connection.Transaction, item);
            DeleteDoodad(connection.Connection, connection.Transaction, doodad.DbId);
        });

    public static bool TrySaveTransfer(Item item, Doodad source, Doodad destination) =>
        TryCommit(connection =>
        {
            ItemPersistence.Save(connection.Connection, connection.Transaction, item);
            destination.Save(connection.Connection, connection.Transaction);
            DeleteDoodad(connection.Connection, connection.Transaction, source.DbId);
        });

    private static bool TryCommit(Action<PersistenceConnection> action)
    {
        try
        {
            using var persistence = MailManager.Instance.DeferPersist();
            return SaveManager.Instance.ExecuteOperation((connection, transaction) =>
            {
                action(new PersistenceConnection(connection, transaction));
                return true;
            });
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist a doodad item lifecycle transition");
            return false;
        }
    }

    private static void DeleteDoodad(MySqlConnection connection, MySqlTransaction transaction, uint doodadId)
    {
        if (doodadId == 0)
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM doodads WHERE id = @id";
        command.Parameters.AddWithValue("@id", doodadId);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    private readonly record struct PersistenceConnection(
        MySqlConnection Connection,
        MySqlTransaction Transaction);
}

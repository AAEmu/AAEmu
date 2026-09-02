using AAEmu.Commons.Network;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Items;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj;

public static class DoodadItemPersistence
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static bool TrySavePlacement(Item item, Doodad doodad) =>
        TryCommit(connection =>
        {
            SaveItem(connection.Connection, connection.Transaction, item);
            doodad.Save(connection.Connection, connection.Transaction);
        });

    public static bool TrySaveRecovery(Item item, Doodad doodad) =>
        TryCommit(connection =>
        {
            SaveItem(connection.Connection, connection.Transaction, item);
            DeleteDoodad(connection.Connection, connection.Transaction, doodad.DbId);
        });

    public static bool TrySaveTransfer(Item item, Doodad source, Doodad destination) =>
        TryCommit(connection =>
        {
            SaveItem(connection.Connection, connection.Transaction, item);
            destination.Save(connection.Connection, connection.Transaction);
            DeleteDoodad(connection.Connection, connection.Transaction, source.DbId);
        });

    private static bool TryCommit(Action<PersistenceConnection> action)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                action(new PersistenceConnection(connection, transaction));
                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackException)
                {
                    Logger.Error(rollbackException, "Failed to roll back a doodad item lifecycle transition");
                }

                throw;
            }

            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist a doodad item lifecycle transition");
            return false;
        }
    }

    private static void SaveItem(MySqlConnection connection, MySqlTransaction transaction, Item item)
    {
        var details = new PacketStream();
        item.WriteDetails(details);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "REPLACE INTO items (" +
            "`id`,`type`,`template_id`,`container_id`,`slot_type`,`slot`,`count`,`detail_type`,`details`,`lifespan_mins`,`made_unit_id`," +
            "`unsecure_time`,`unpack_time`,`owner`,`created_at`,`grade`,`flags`,`ucc`," +
            "`expire_time`,`expire_online_minutes`,`charge_time`,`charge_count`) VALUES (" +
            "@id,@type,@template_id,@container_id,@slot_type,@slot,@count,@detail_type,@details,@lifespan_mins,@made_unit_id," +
            "@unsecure_time,@unpack_time,@owner,@created_at,@grade,@flags,@ucc," +
            "@expire_time,@expire_online_minutes,@charge_time,@charge_count)";
        command.Parameters.AddWithValue("@id", item.Id);
        command.Parameters.AddWithValue("@type", item.GetType().ToString());
        command.Parameters.AddWithValue("@template_id", item.TemplateId);
        command.Parameters.AddWithValue("@container_id", item._holdingContainer?.ContainerId ?? 0);
        command.Parameters.AddWithValue("@slot_type", (int)item.SlotType);
        command.Parameters.AddWithValue("@slot", item.Slot);
        command.Parameters.AddWithValue("@count", item.Count);
        command.Parameters.AddWithValue("@detail_type", (byte)item.DetailType);
        command.Parameters.AddWithValue("@details", details.GetBytes());
        command.Parameters.AddWithValue("@lifespan_mins", item.LifespanMins);
        command.Parameters.AddWithValue("@made_unit_id", item.MadeUnitId);
        command.Parameters.AddWithValue("@unsecure_time", item.UnsecureTime);
        command.Parameters.AddWithValue("@unpack_time", item.UnpackTime);
        command.Parameters.AddWithValue("@owner", item.OwnerId);
        command.Parameters.AddWithValue("@created_at", item.CreateTime);
        command.Parameters.AddWithValue("@grade", item.Grade);
        command.Parameters.AddWithValue("@flags", (byte)item.ItemFlags);
        command.Parameters.AddWithValue("@ucc", item.UccId);
        command.Parameters.AddWithValue("@expire_time", item.ExpirationTime);
        command.Parameters.AddWithValue("@expire_online_minutes", item.ExpirationOnlineMinutesLeft);
        command.Parameters.AddWithValue("@charge_time", item.ChargeStartTime);
        command.Parameters.AddWithValue("@charge_count", item.ChargeCount);
        command.Prepare();
        if (command.ExecuteNonQuery() < 1)
            throw new InvalidOperationException($"Failed to persist item {item.Id}");
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

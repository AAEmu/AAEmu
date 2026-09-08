using AAEmu.Commons.Network;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Items;

internal static class ItemPersistence
{
    public static void Save(MySqlConnection connection, MySqlTransaction transaction, Item item)
        => Write(connection, transaction, item, false);

    public static void Insert(MySqlConnection connection, MySqlTransaction transaction, Item item)
        => Write(connection, transaction, item, true);

    private static void Write(MySqlConnection connection, MySqlTransaction transaction, Item item, bool insert)
    {
        var details = new PacketStream();
        item.WriteDetails(details);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = (insert ? "INSERT" : "REPLACE") + " INTO items (" +
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
}

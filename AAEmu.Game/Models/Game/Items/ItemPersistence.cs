using System.Data.Common;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items;

internal static class ItemPersistence
{
    public static void Save(DbConnection connection, DbTransaction transaction, Item item)
        => Write(connection, transaction, item, false);

    public static void Insert(DbConnection connection, DbTransaction transaction, Item item)
        => Write(connection, transaction, item, true);

    private static void Write(DbConnection connection, DbTransaction transaction, Item item, bool insert)
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
        command.AddParameter("@id", item.Id);
        command.AddParameter("@type", item.GetType().ToString());
        command.AddParameter("@template_id", item.TemplateId);
        command.AddParameter("@container_id", item._holdingContainer?.ContainerId ?? 0);
        command.AddParameter("@slot_type", (int)item.SlotType);
        command.AddParameter("@slot", item.Slot);
        command.AddParameter("@count", item.Count);
        command.AddParameter("@detail_type", (byte)item.DetailType);
        command.AddParameter("@details", details.GetBytes());
        command.AddParameter("@lifespan_mins", item.LifespanMins);
        command.AddParameter("@made_unit_id", item.MadeUnitId);
        command.AddParameter("@unsecure_time", item.UnsecureTime);
        command.AddParameter("@unpack_time", item.UnpackTime);
        command.AddParameter("@owner", item.OwnerId);
        command.AddParameter("@created_at", item.CreateTime);
        command.AddParameter("@grade", item.Grade);
        command.AddParameter("@flags", (byte)item.ItemFlags);
        command.AddParameter("@ucc", item.UccId);
        command.AddParameter("@expire_time", item.ExpirationTime);
        command.AddParameter("@expire_online_minutes", item.ExpirationOnlineMinutesLeft);
        command.AddParameter("@charge_time", item.ChargeStartTime);
        command.AddParameter("@charge_count", item.ChargeCount);
        command.Prepare();
        if (command.ExecuteNonQuery() < 1)
            throw new InvalidOperationException($"Failed to persist item {item.Id}");
    }
}

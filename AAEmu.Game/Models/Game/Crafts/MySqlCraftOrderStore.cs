using AAEmu.Commons.Utils.DB;

using NLog;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>MySQL store for the live board. Each mutate is its own connection so a World kill cannot leave escrow without a row.</summary>
public sealed class MySqlCraftOrderStore : ICraftOrderStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string SelectAll = """
        SELECT id, owner_id, owner_name, owner_world_char_key, craft_id, item_id, grade, `count`,
               fee, actability_group_id, actability_point, posted_unix, expires_unix, status, kind
        FROM craft_orders
        """;

    private const string InsertSql = """
        INSERT INTO craft_orders
            (id, owner_id, owner_name, owner_world_char_key, craft_id, item_id, grade, `count`,
             fee, actability_group_id, actability_point, posted_unix, expires_unix, status, kind)
        VALUES
            (@id, @owner_id, @owner_name, @owner_world_char_key, @craft_id, @item_id, @grade, @count,
             @fee, @actability_group_id, @actability_point, @posted_unix, @expires_unix, @status, @kind)
        """;

    public IReadOnlyList<CraftOrder> LoadAll()
    {
        var rows = new List<CraftOrder>();
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectAll;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CraftOrder
            {
                Id = reader.GetUInt64("id"),
                OwnerId = reader.GetUInt32("owner_id"),
                OwnerName = reader.GetString("owner_name"),
                OwnerWorldCharKey = reader.GetUInt64("owner_world_char_key"),
                CraftId = reader.GetUInt32("craft_id"),
                ItemId = reader.GetUInt32("item_id"),
                Grade = reader.GetByte("grade"),
                Count = reader.GetUInt32("count"),
                Fee = reader.GetUInt64("fee"),
                ActabilityGroupId = reader.GetUInt32("actability_group_id"),
                ActabilityPoint = reader.GetUInt32("actability_point"),
                PostedUnix = reader.GetInt64("posted_unix"),
                ExpiresUnix = reader.GetInt64("expires_unix"),
                Status = reader.GetByte("status"),
                Kind = reader.GetByte("kind")
            });
        }

        return rows;
    }

    public bool Insert(CraftOrder order)
    {
        if (order == null || order.Id == 0)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = InsertSql;
            command.Parameters.AddWithValue("@id", order.Id);
            command.Parameters.AddWithValue("@owner_id", order.OwnerId);
            command.Parameters.AddWithValue("@owner_name", order.OwnerName ?? string.Empty);
            command.Parameters.AddWithValue("@owner_world_char_key", order.OwnerWorldCharKey);
            command.Parameters.AddWithValue("@craft_id", order.CraftId);
            command.Parameters.AddWithValue("@item_id", order.ItemId);
            command.Parameters.AddWithValue("@grade", order.Grade);
            command.Parameters.AddWithValue("@count", order.Count);
            command.Parameters.AddWithValue("@fee", order.Fee);
            command.Parameters.AddWithValue("@actability_group_id", order.ActabilityGroupId);
            command.Parameters.AddWithValue("@actability_point", order.ActabilityPoint);
            command.Parameters.AddWithValue("@posted_unix", order.PostedUnix);
            command.Parameters.AddWithValue("@expires_unix", order.ExpiresUnix);
            command.Parameters.AddWithValue("@status", order.Status);
            command.Parameters.AddWithValue("@kind", order.Kind);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Craft order: failed to insert order {0}", order.Id);
            return false;
        }
    }

    public bool Delete(ulong orderId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM craft_orders WHERE id = @id";
            command.Parameters.AddWithValue("@id", orderId);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Craft order: failed to delete order {0}", orderId);
            return false;
        }
    }

    public bool DeleteAll()
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM craft_orders";
            command.ExecuteNonQuery();
            command.CommandText = "DELETE FROM craft_order_fee_stats";
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Craft order: failed to clear the board");
            return false;
        }
    }

    public IReadOnlyList<CraftOrderFeeStat> LoadFeeStats()
    {
        var rows = new List<CraftOrderFeeStat>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT craft_id, lowest, highest FROM craft_order_fee_stats";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new CraftOrderFeeStat(
                    reader.GetUInt32("craft_id"),
                    reader.GetUInt64("lowest"),
                    reader.GetUInt64("highest")));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Craft order: failed to load fee stats");
        }

        return rows;
    }

    public bool UpsertFeeStats(CraftOrderFeeStat stat)
    {
        if (stat.CraftId == 0)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO craft_order_fee_stats (craft_id, lowest, highest)
                VALUES (@craft_id, @lowest, @highest)
                ON DUPLICATE KEY UPDATE lowest = @lowest, highest = @highest
                """;
            command.Parameters.AddWithValue("@craft_id", stat.CraftId);
            command.Parameters.AddWithValue("@lowest", stat.Lowest);
            command.Parameters.AddWithValue("@highest", stat.Highest);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Craft order: failed to persist fee stats for craft {0}", stat.CraftId);
            return false;
        }
    }
}

using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;

using NLog;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// MySQL store for the per-character reopen-box states. Writes happen at box open, roll and
/// claim (same persist-first shape as the random shop store): the counter spend and the settled
/// flip are conditional updates whose affected-row count is the exactly-once gate, in-process
/// and across processes alike.
/// </summary>
public sealed class MySqlReopenBoxStore : IReopenBoxStateStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string StateColumns =
        "character_id, item_id, pack_id, free_used, charge_used, rolled_at, refresh_available_at, " +
        "opened_at, group_id, good_id, reward_item_id, reward_grade, reward_count, settled";

    public IReadOnlyList<ReopenBoxState> LoadAll()
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {StateColumns} FROM character_reopen_boxes";
            using var reader = command.ExecuteReader();
            var states = new List<ReopenBoxState>();
            while (reader.Read())
            {
                states.Add(new ReopenBoxState
                {
                    CharacterId = reader.GetUInt32("character_id"),
                    ItemId = reader.GetInt64("item_id"),
                    PackId = reader.GetUInt32("pack_id"),
                    FreeUsed = reader.GetInt32("free_used"),
                    ChargeUsed = reader.GetInt32("charge_used"),
                    RolledAt = ServerCalendar.AsUtc(reader.GetDateTime("rolled_at")),
                    RefreshAvailableAt = ServerCalendar.AsUtc(reader.GetDateTime("refresh_available_at")),
                    OpenedAt = reader.IsDBNull(reader.GetOrdinal("opened_at"))
                        ? null
                        : ServerCalendar.AsUtc(reader.GetDateTime("opened_at")),
                    GroupId = reader.GetUInt32("group_id"),
                    GoodId = reader.GetUInt32("good_id"),
                    RewardItemId = reader.GetUInt32("reward_item_id"),
                    RewardGrade = reader.GetByte("reward_grade"),
                    RewardCount = reader.GetInt32("reward_count"),
                    Settled = reader.GetBoolean("settled")
                });
            }
            return states;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: failed to load persisted box states");
            return [];
        }
    }

    public bool Save(ReopenBoxState state)
    {
        if (state == null)
            return false;
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO character_reopen_boxes (" + StateColumns + ") " +
                "VALUES (@character_id, @item_id, @pack_id, @free_used, @charge_used, @rolled_at, " +
                "@refresh_available_at, @opened_at, @group_id, @good_id, @reward_item_id, @reward_grade, " +
                "@reward_count, @settled) " +
                "ON DUPLICATE KEY UPDATE pack_id = @pack_id, free_used = @free_used, charge_used = @charge_used, " +
                "rolled_at = @rolled_at, refresh_available_at = @refresh_available_at, opened_at = @opened_at, " +
                "group_id = @group_id, good_id = @good_id, reward_item_id = @reward_item_id, " +
                "reward_grade = @reward_grade, reward_count = @reward_count, settled = @settled";
            command.Parameters.AddWithValue("@character_id", state.CharacterId);
            command.Parameters.AddWithValue("@item_id", state.ItemId);
            command.Parameters.AddWithValue("@pack_id", state.PackId);
            command.Parameters.AddWithValue("@free_used", state.FreeUsed);
            command.Parameters.AddWithValue("@charge_used", state.ChargeUsed);
            command.Parameters.AddWithValue("@rolled_at", state.RolledAt);
            command.Parameters.AddWithValue("@refresh_available_at", state.RefreshAvailableAt);
            command.Parameters.AddWithValue("@opened_at",
                state.OpenedAt.HasValue ? state.OpenedAt.Value : DBNull.Value);
            command.Parameters.AddWithValue("@group_id", state.GroupId);
            command.Parameters.AddWithValue("@good_id", state.GoodId);
            command.Parameters.AddWithValue("@reward_item_id", state.RewardItemId);
            command.Parameters.AddWithValue("@reward_grade", state.RewardGrade);
            command.Parameters.AddWithValue("@reward_count", state.RewardCount);
            command.Parameters.AddWithValue("@settled", state.Settled);
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: failed to save box state for character {0} item {1}",
                state.CharacterId, state.ItemId);
            return false;
        }
    }

    public bool TrySpendOpen(uint characterId, long itemId, bool isCharge, int max)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            // Increment only while the counter is below the pack max for this box: the affected
            // row count tells the caller the allowance was exhausted.
            var column = isCharge ? "charge_used" : "free_used";
            command.CommandText =
                $"UPDATE character_reopen_boxes SET {column} = {column} + 1 " +
                "WHERE character_id = @character_id AND item_id = @item_id AND " + column + " < @max";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@item_id", itemId);
            command.Parameters.AddWithValue("@max", max);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: failed to spend an open for character {0} item {1}",
                characterId, itemId);
            return false;
        }
    }

    public bool ReleaseOpen(uint characterId, long itemId, bool isCharge)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            var column = isCharge ? "charge_used" : "free_used";
            command.CommandText =
                $"UPDATE character_reopen_boxes SET {column} = {column} - 1 " +
                "WHERE character_id = @character_id AND item_id = @item_id AND " + column + " > 0";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@item_id", itemId);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: failed to release an open for character {0} item {1}",
                characterId, itemId);
            return false;
        }
    }

    public bool TrySettle(uint characterId, long itemId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE character_reopen_boxes SET settled = 1 " +
                "WHERE character_id = @character_id AND item_id = @item_id AND settled = 0";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@item_id", itemId);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: failed to settle the roll for character {0} item {1}",
                characterId, itemId);
            return false;
        }
    }

    public bool ReleaseSettle(uint characterId, long itemId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE character_reopen_boxes SET settled = 0 " +
                "WHERE character_id = @character_id AND item_id = @item_id AND settled = 1";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@item_id", itemId);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: failed to release the roll claim for character {0} item {1}",
                characterId, itemId);
            return false;
        }
    }
}

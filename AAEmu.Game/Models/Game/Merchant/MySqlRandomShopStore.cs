using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Items;

using NLog;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// MySQL store for the per-character random shop windows. Writes happen at window roll, refresh
/// and buy (same persist-first shape as the craft order board), so a World kill cannot lose a sale
/// or hand out a sold offer twice. The claim and the refresh spend are conditional updates: the
/// affected-row count is the exactly-once gate, in-process and across processes alike.
/// </summary>
public sealed class MySqlRandomShopStore : IRandomShopStateStore
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string WindowColumns =
        "character_id, pack_id, period_start, rolled_at, free_used, charge_used";

    private const string OfferColumns =
        "character_id, pack_id, slot, group_id, good_id, item_id, grade, cost, currency, sold";

    public IReadOnlyList<RandomShopWindow> LoadAll()
    {
        var windows = new Dictionary<(uint CharacterId, uint PackId), RandomShopWindow>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT {WindowColumns} FROM character_random_shop_windows";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var window = new RandomShopWindow
                    {
                        CharacterId = reader.GetUInt32("character_id"),
                        PackId = reader.GetUInt32("pack_id"),
                        PeriodStart = ServerCalendar.AsUtc(reader.GetDateTime("period_start")),
                        RolledAt = ServerCalendar.AsUtc(reader.GetDateTime("rolled_at")),
                        FreeUsed = reader.GetInt32("free_used"),
                        ChargeUsed = reader.GetInt32("charge_used")
                    };
                    windows[(window.CharacterId, window.PackId)] = window;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT {OfferColumns} FROM character_random_shop_offers ORDER BY character_id, pack_id, slot";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var key = (reader.GetUInt32("character_id"), reader.GetUInt32("pack_id"));
                    if (!windows.TryGetValue(key, out var window))
                        continue;
                    window.Offers.Add(new RandomShopOffer
                    {
                        GroupId = reader.GetUInt32("group_id"),
                        GoodId = reader.GetUInt32("good_id"),
                        Slot = reader.GetInt32("slot"),
                        ItemId = reader.GetUInt32("item_id"),
                        Grade = reader.GetByte("grade"),
                        Cost = reader.GetInt32("cost"),
                        Currency = (ShopCurrencyType)reader.GetByte("currency"),
                        Sold = reader.GetBoolean("sold")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Random shop: failed to load persisted windows");
            return [];
        }

        return windows.Values.ToList();
    }

    /// <summary>Header upsert plus a full offer-row replace in one transaction (contract: atomic).</summary>
    public bool SaveWindow(RandomShopWindow window)
    {
        if (window == null)
            return false;
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText =
                "INSERT INTO character_random_shop_windows (" + WindowColumns + ") " +
                "VALUES (@character_id, @pack_id, @period_start, @rolled_at, @free_used, @charge_used) " +
                "ON DUPLICATE KEY UPDATE period_start = @period_start, rolled_at = @rolled_at, " +
                "free_used = @free_used, charge_used = @charge_used";
            command.Parameters.AddWithValue("@character_id", window.CharacterId);
            command.Parameters.AddWithValue("@pack_id", window.PackId);
            command.Parameters.AddWithValue("@period_start", window.PeriodStart);
            command.Parameters.AddWithValue("@rolled_at", window.RolledAt);
            command.Parameters.AddWithValue("@free_used", window.FreeUsed);
            command.Parameters.AddWithValue("@charge_used", window.ChargeUsed);
            command.ExecuteNonQuery();

            command.Parameters.Clear();
            command.CommandText = "DELETE FROM character_random_shop_offers WHERE character_id = @character_id AND pack_id = @pack_id";
            command.Parameters.AddWithValue("@character_id", window.CharacterId);
            command.Parameters.AddWithValue("@pack_id", window.PackId);
            command.ExecuteNonQuery();

            foreach (var offer in window.Offers)
            {
                command.Parameters.Clear();
                command.CommandText =
                    "INSERT INTO character_random_shop_offers (" + OfferColumns + ") " +
                    "VALUES (@character_id, @pack_id, @slot, @group_id, @good_id, @item_id, @grade, @cost, @currency, @sold)";
                command.Parameters.AddWithValue("@character_id", window.CharacterId);
                command.Parameters.AddWithValue("@pack_id", window.PackId);
                command.Parameters.AddWithValue("@slot", offer.Slot);
                command.Parameters.AddWithValue("@group_id", offer.GroupId);
                command.Parameters.AddWithValue("@good_id", offer.GoodId);
                command.Parameters.AddWithValue("@item_id", offer.ItemId);
                command.Parameters.AddWithValue("@grade", offer.Grade);
                command.Parameters.AddWithValue("@cost", offer.Cost);
                command.Parameters.AddWithValue("@currency", (byte)offer.Currency);
                command.Parameters.AddWithValue("@sold", offer.Sold);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Random shop: failed to save window for character {0}, pack {1}",
                window.CharacterId, window.PackId);
            return false;
        }
    }

    public bool TryClaimOffer(uint characterId, uint packId, int slot)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            // sold 0 -> 1 only: the affected-row count is the exactly-once gate.
            command.CommandText =
                "UPDATE character_random_shop_offers SET sold = 1 " +
                "WHERE character_id = @character_id AND pack_id = @pack_id AND slot = @slot AND sold = 0";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@pack_id", packId);
            command.Parameters.AddWithValue("@slot", slot);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Random shop: failed to claim offer {2} of pack {1} for character {0}",
                characterId, packId, slot);
            return false;
        }
    }

    public bool ReleaseOffer(uint characterId, uint packId, int slot)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE character_random_shop_offers SET sold = 0 " +
                "WHERE character_id = @character_id AND pack_id = @pack_id AND slot = @slot AND sold = 1";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@pack_id", packId);
            command.Parameters.AddWithValue("@slot", slot);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Random shop: failed to release offer {2} of pack {1} for character {0}",
                characterId, packId, slot);
            return false;
        }
    }

    public bool TrySpendRefresh(uint characterId, uint packId, DateTime periodStart, bool isFree, int max)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            // Increment only while the counter is below the pack max for this period: the affected
            // row count tells the caller the allowance was exhausted (0) or the period moved on.
            command.CommandText =
                "UPDATE character_random_shop_windows SET " + (isFree ? "free_used" : "charge_used") + " = " +
                (isFree ? "free_used" : "charge_used") + " + 1 " +
                "WHERE character_id = @character_id AND pack_id = @pack_id AND period_start = @period_start AND " +
                (isFree ? "free_used" : "charge_used") + " < @max";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@pack_id", packId);
            command.Parameters.AddWithValue("@period_start", periodStart);
            command.Parameters.AddWithValue("@max", max);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Random shop: failed to spend a {2} refresh for character {0}, pack {1}",
                characterId, packId, isFree ? "free" : "paid");
            return false;
        }
    }

    public bool ReleaseRefresh(uint characterId, uint packId, DateTime periodStart, bool isFree)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE character_random_shop_windows SET " + (isFree ? "free_used" : "charge_used") + " = " +
                (isFree ? "free_used" : "charge_used") + " - 1 " +
                "WHERE character_id = @character_id AND pack_id = @pack_id AND period_start = @period_start AND " +
                (isFree ? "free_used" : "charge_used") + " > 0";
            command.Parameters.AddWithValue("@character_id", characterId);
            command.Parameters.AddWithValue("@pack_id", packId);
            command.Parameters.AddWithValue("@period_start", periodStart);
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Random shop: failed to release a {2} refresh for character {0}, pack {1}",
                characterId, packId, isFree ? "free" : "paid");
            return false;
        }
    }
}

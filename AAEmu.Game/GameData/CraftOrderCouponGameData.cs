using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// <c>craft_order_coupons</c> — Instant complete's ticket bands, in content id order so the
/// first covering band wins.
/// </summary>
[GameData]
public class CraftOrderCouponGameData : Singleton<CraftOrderCouponGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private List<CraftOrderCoupon> _coupons = [];

    public IReadOnlyList<CraftOrderCoupon> Coupons => _coupons;

    /// <summary>Listing lifetime from the coupon table's top hour. Zero when the table is empty.</summary>
    public TimeSpan ListingLifetime => CraftOrderCouponRules.ListingLifetime(_coupons);

    public void Load(SqliteConnection connection)
    {
        var rows = new List<(uint Id, CraftOrderCoupon Coupon)>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, item_id, amount, min, max
            FROM craft_order_coupons
            ORDER BY id
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            rows.Add((
                reader.GetUInt32("id"),
                new CraftOrderCoupon(
                    reader.GetUInt32("item_id"),
                    reader.GetInt32("amount"),
                    reader.GetInt32("min"),
                    reader.GetInt32("max"))));
        }

        _coupons = rows.Select(row => row.Coupon).ToList();
        Logger.Info("Loaded {0} craft order coupon band(s)", _coupons.Count);
    }

    public void PostLoad()
    {
    }

    /// <summary>Replaces the table for tests that cannot open compact.</summary>
    public void SetForTest(IReadOnlyList<CraftOrderCoupon> coupons)
    {
        _coupons = coupons == null ? [] : [.. coupons];
    }
}

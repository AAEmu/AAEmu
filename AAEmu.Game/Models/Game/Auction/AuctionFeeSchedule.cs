using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Models.Game.Auction;

/// <summary>
/// The auction house's economy, read from <c>compact.sqlite3</c>.
///
/// Every rate here used to be a literal in AuctionManager — a 1%-per-duration-step listing deposit, a flat
/// 10% sale commission, a 10% cancellation charge and a 1000000 copper cap, one of them carrying a
/// "TODO: Read this from saved data" comment. The client ships all of it in <c>content_configs</c> under
/// <c>kind_id = 5</c>, named through <c>enum_content_configs</c>:
///
///   auction_charge                          200        sale commission
///   auction_deposit_6 / _12 / _24 / _48     5/10/15/20 listing deposit per AuctionDuration step
///   auction_deposit_max                     1000000    deposit cap, in copper
///   auction_charge_pcbang_discount          0
///   auction_deposit_pcbang_discount         0
///   auction_charge_account_buff_discount    50
///   auction_deposit_account_buff_discount   80
///
/// <c>auction_deposit_max</c> is 1000000, matching the literal the server already used, which is what ties
/// this row set to these formulas.
/// </summary>
public class AuctionFeeSchedule
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Divisor turning a stored deposit rate into a fraction.
    ///
    /// The deposit rates are per-mille. The client's own listing UI proves it: <c>putup.lua</c> reads the raw
    /// value through <c>X2Auction:GetDepositRatioValue</c> and formats it as <c>ratio / 10</c>, which
    /// <c>locale_helper.lua</c> then feeds to the <c>deposit_tooltip_rate_by_time</c> string documented in
    /// source as "· $1: $2% of the listing price". Raw over ten is therefore the percentage, so raw over a
    /// thousand is the fraction, and the shipped 5/10/15/20 are 0.5%, 1%, 1.5% and 2% for the four durations.
    /// </summary>
    private const int DepositRateDivisor = 1000;

    /// <summary>
    /// Divisor turning the stored sale commission into a fraction.
    ///
    /// Not the same scale as the deposit. The commission reaches Lua already converted — <c>putup.lua</c>
    /// prints <c>chargeInfo["chargeRate"]</c> straight into a "%s%%" with no arithmetic — so the raw-to-percent
    /// factor lives in the native <c>X2Auction:GetChargeInfo</c> and cannot be read off the UI the way the
    /// deposit can. The per-item overrides pin it instead: <c>items.auction_charge</c> is 0 for all but 34
    /// rows, and those carry 100, 500 or 1000. Over ten thousand that is 1%, 5% and 10%, with the global
    /// default of 200 at 2%. The deposit's per-mille scale would instead make those rows a 10%, 50% and 100%
    /// cut, and a commission that takes the entire sale price is not a rate the client would ship.
    /// </summary>
    private const int ChargeRateDivisor = 10000;

    /// <summary>Discount values are plain percentages, unlike the rates above.</summary>
    private const int DiscountDivisor = 100;

    private readonly Dictionary<string, int> _values = [];

    public int SaleChargeRate => Get("auction_charge", 200);
    public int DepositMax => Get("auction_deposit_max", 1000000);
    public int SaleChargeAccountBuffDiscount => Get("auction_charge_account_buff_discount", 0);
    public int DepositAccountBuffDiscount => Get("auction_deposit_account_buff_discount", 0);

    private int Get(string name, int fallback) => _values.GetValueOrDefault(name, fallback);

    /// <summary>Stored deposit rate for a listing duration.</summary>
    public int GetDepositRate(AuctionDuration duration)
    {
        var key = duration switch
        {
            AuctionDuration.AuctionDuration6Hours => "auction_deposit_6",
            AuctionDuration.AuctionDuration12Hours => "auction_deposit_12",
            AuctionDuration.AuctionDuration24Hours => "auction_deposit_24",
            AuctionDuration.AuctionDuration48Hours => "auction_deposit_48",
            _ => null
        };

        // A duration byte off the wire that is not one of the four shipped steps has no deposit row; charge
        // the longest listing rather than falling through to a free one.
        return key == null
            ? Get("auction_deposit_48", 20)
            : Get(key, duration switch
            {
                AuctionDuration.AuctionDuration6Hours => 5,
                AuctionDuration.AuctionDuration12Hours => 10,
                AuctionDuration.AuctionDuration24Hours => 15,
                _ => 20
            });
    }

    /// <summary>
    /// Commission the house keeps from a completed sale. <paramref name="itemChargeRate"/> is
    /// <c>items.auction_charge</c> when that item overrides the global rate, 0 to use the default.
    /// </summary>
    public long GetSaleCharge(long soldAmount, int itemChargeRate = 0, int discountPercent = 0)
    {
        if (soldAmount <= 0)
            return 0;

        var rate = itemChargeRate > 0 ? itemChargeRate : SaleChargeRate;
        rate = ApplyPercentDiscount(rate, discountPercent);
        var charge = soldAmount * rate / ChargeRateDivisor;
        return Math.Clamp(charge, 0, soldAmount);
    }

    public long GetListingDeposit(long buyoutPrice, AuctionDuration duration, int discountPercent = 0)
    {
        if (buyoutPrice <= 0)
            return 0;

        var rate = ApplyPercentDiscount(GetDepositRate(duration), discountPercent);
        var deposit = buyoutPrice * rate / DepositRateDivisor;
        return Math.Clamp(deposit, 0, DepositMax);
    }

    public static int ApplyPercentDiscount(int rate, int discountPercent)
    {
        if (rate <= 0 || discountPercent <= 0)
            return rate;
        var discounted = (long)rate * (DiscountDivisor - Math.Clamp(discountPercent, 0, DiscountDivisor)) / DiscountDivisor;
        return (int)Math.Max(0, discounted);
    }

    public void Load()
    {
        _values.Clear();

        using var connection = SQLite.CreateConnection();
        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT e.name, c.value FROM content_configs c " +
            "JOIN enum_content_configs e ON e.id = c.id " +
            "WHERE c.kind_id = 5";
        command.Prepare();

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            _values[reader.GetString("name")] = reader.GetInt32("value");
        }

        Logger.Info($"Loaded {_values.Count} auction economy values");
    }
}

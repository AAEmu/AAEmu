using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using MySql.Data.MySqlClient;
using NLog;
using System.Data;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The per-day count of how many times each item template has been sold to a vendor, for the
/// templates that carry a sale limit (<c>items.one_time_sale</c> or <c>items.limited_sale_count</c>).
/// <para>
/// The counter is per item template rather than per character, because that is what the limit
/// means: a limited item's daily allowance is a property of the item, so a second character cannot
/// spend the first one's. The day it belongs to is written next to the count, so an allowance
/// comes back on its own once the date moves - no reset sweep has to survive a restart to be
/// correct, and a server that was down across midnight does not keep yesterday's spent allowances.
/// </para>
/// <para>
/// Templates with no limit are never written here: the shipped catalogue has 131 of them out of
/// 51010, so a counter row for every sale would be a table that grows with the economy rather than
/// with the content.
/// </para>
/// </summary>
public class ItemSaleLimitManager : Singleton<ItemSaleLimitManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new();
    private readonly Dictionary<uint, int> _soldOnDay = [];

    /// <summary>The UTC date the in-memory counts belong to. Null until the first read.</summary>
    private DateTime? _day;

    /// <summary>
    /// How many times this item template has been sold to a vendor on the current UTC day.
    /// Templates with no sale limit are never counted, so this is 0 for almost all of them.
    /// </summary>
    public int GetSoldToday(uint itemTemplateId)
    {
        lock (_lock)
        {
            RollDay(DateTime.UtcNow);
            return _soldOnDay.GetValueOrDefault(itemTemplateId);
        }
    }

    /// <summary>
    /// Decides whether a vendor may take one more stack of this template and, when the answer is
    /// yes, records the sale so a second call in the same day sees it.
    /// </summary>
    /// <param name="template">The item template being sold.</param>
    /// <param name="decision">
    /// The decision, with <c>Allowed</c> flipped to false and the reason left intact when the sale
    /// was refused.
    /// </param>
    /// <returns><c>true</c> when the sale was allowed and has been recorded.</returns>
    public bool TryConsumeSale(ItemTemplate template, out ItemSaleDecision decision)
    {
        decision = ItemSaleRules.Evaluate(template, GetSoldToday(template?.Id ?? 0));
        if (!decision.Allowed || !ItemSaleRules.HasDailySaleLimit(template))
            return decision.Allowed;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            RollDay(now);
            var sold = _soldOnDay.GetValueOrDefault(template.Id) + 1;
            _soldOnDay[template.Id] = sold;
            Persist(template.Id, sold, now.Date);
            decision = ItemSaleRules.Evaluate(template, sold);
        }

        return decision.Allowed;
    }

    /// <summary>
    /// Reads the stored counters back. A stored row whose day is not today is not a limit that is
    /// still owed - it is yesterday's, and the allowance is available again.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            _soldOnDay.Clear();
            _day = null;
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT item_id, day_key, sold_count FROM item_sale_counts WHERE day_key = @day_key";
                command.Parameters.AddWithValue("@day_key", DateTime.UtcNow.Date);
                command.Prepare();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    _soldOnDay[reader.GetUInt32(0)] = reader.GetInt32(2);
                _day = DateTime.UtcNow.Date;
            }
            catch (Exception exception)
            {
                // Without the table the world cannot honour a daily sale limit, and saying so is
                // better than quietly selling the limited items without a limit.
                Logger.Error(exception, "Failed to load the per-day item sale counts; limited sales will be refused");
            }
        }
    }

    /// <summary>Drops the in-memory counts. The stored rows are already keyed by their own day.</summary>
    public void ResetDaily()
    {
        lock (_lock)
        {
            _soldOnDay.Clear();
            _day = null;
        }
    }

    /// <summary>
    /// Moves the in-memory counts onto the day they now belong to. A day that has rolled clears
    /// them, which is the reset; the stored rows are left alone because each carries its own day.
    /// </summary>
    private void RollDay(DateTime now)
    {
        var today = now.Date;
        if (_day == today)
            return;
        _day = today;
        _soldOnDay.Clear();
    }

    private static void Persist(uint itemTemplateId, int sold, DateTime day)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO item_sale_counts (item_id, day_key, sold_count) " +
            "VALUES (@item_id, @day_key, @sold_count) " +
            "ON DUPLICATE KEY UPDATE day_key = VALUES(day_key), sold_count = VALUES(sold_count)";
        command.Parameters.AddWithValue("@item_id", itemTemplateId);
        command.Parameters.AddWithValue("@day_key", day);
        command.Parameters.AddWithValue("@sold_count", sold);
        command.Prepare();
        if (command.ExecuteNonQuery() < 1)
            throw new InvalidOperationException($"Failed to record the sale of item {itemTemplateId}");
        transaction.Commit();
    }
}

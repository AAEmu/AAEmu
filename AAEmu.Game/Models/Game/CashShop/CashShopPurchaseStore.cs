using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.CashShop;

public enum CashShopPersistenceFailureReason
{
    None,
    InvalidContent,
    InsufficientCredits,
    InsufficientAaPoints,
    InsufficientLoyalty,
    InsufficientCoins,
    SoldOut,
    AuditWrite,
    DeliveryFailed,
    PersistenceUnavailable
}

public sealed record CashShopPurchaseCommit(
    uint BuyerAccountId,
    uint BuyerCharacterId,
    uint TargetAccountId,
    uint TargetCharacterId,
    DateTime SaleDateUtc,
    CashShopPurchasePlan Plan,
    IReadOnlyList<BaseMail> Mails,
    long LiveMoney,
    long LiveAaPoints);

public sealed record CashShopPurchaseStoreResult(
    bool Succeeded,
    CashShopPersistenceFailureReason Reason,
    long Credits,
    long Loyalty,
    long Money,
    long AaPoints,
    uint FailedShopId,
    IReadOnlyDictionary<uint, int> RemainingByShop)
{
    public CashShopPurchaseFailure ClientFailure => Reason switch
    {
        CashShopPersistenceFailureReason.InsufficientCredits => new(
            CashShopPurchaseFailureReason.InsufficientCredits, 0,
            ErrorMessageType.IngameShopNotEnoughAaCash, ErrorMessageType.IngameShopNotEnoughAaCash),
        CashShopPersistenceFailureReason.InsufficientAaPoints => new(
            CashShopPurchaseFailureReason.InsufficientAaPoints, 0,
            ErrorMessageType.IngameShopNotEnoughAaPoint, ErrorMessageType.IngameShopNotEnoughAaPoint),
        CashShopPersistenceFailureReason.InsufficientLoyalty => new(
            CashShopPurchaseFailureReason.InsufficientLoyalty, 0,
            ErrorMessageType.IngameShopNotEnoughBmMileage, ErrorMessageType.IngameShopNotEnoughBmMileage),
        CashShopPersistenceFailureReason.InsufficientCoins => new(
            CashShopPurchaseFailureReason.InsufficientCoins, 0,
            ErrorMessageType.IngameShopBuyFail, ErrorMessageType.NotEnoughCoin),
        CashShopPersistenceFailureReason.SoldOut => new(
            CashShopPurchaseFailureReason.SoldOut, FailedShopId,
            ErrorMessageType.IngameShopSoldOut, ErrorMessageType.IngameShopSoldOut),
        _ => new(CashShopPurchaseFailureReason.InvalidContent)
    };
}

/// <summary>
/// Produces a process-scoped, opaque correlation value for purchase diagnostics. The account and
/// character identifiers are hashed with a per-process salt and are never returned or logged.
/// </summary>
internal static class CashShopLogCorrelation
{
    private static readonly byte[] ProcessSalt = RandomNumberGenerator.GetBytes(32);

    public static string ForBuyer(uint accountId, uint characterId)
    {
        var input = new byte[ProcessSalt.Length + (sizeof(uint) * 2)];
        ProcessSalt.CopyTo(input, 0);
        BitConverter.TryWriteBytes(input.AsSpan(ProcessSalt.Length, sizeof(uint)), accountId);
        BitConverter.TryWriteBytes(input.AsSpan(ProcessSalt.Length + sizeof(uint), sizeof(uint)), characterId);
        var digest = SHA256.HashData(input);
        return Convert.ToHexString(digest.AsSpan(0, 8));
    }
}

/// <summary>
/// Stages the database side of an ICS purchase on a caller-owned transaction. The caller commits
/// only after every balance, stock, audit row, and mail delivery has been staged successfully.
/// </summary>
public static class CashShopPurchaseStore
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static CashShopPurchaseStoreResult Stage(
        DbConnection connection,
        DbTransaction transaction,
        CashShopPurchaseCommit commit,
        Func<BaseMail, bool> stageMail)
    {
        if (connection == null || transaction == null || commit?.Plan is not { Lines.Count: > 0 } plan ||
            commit.BuyerAccountId == 0 || commit.BuyerCharacterId == 0 ||
            commit.TargetAccountId == 0 || commit.TargetCharacterId == 0 ||
            commit.Mails is not { Count: > 0 } || stageMail == null)
            return Failure(CashShopPersistenceFailureReason.InvalidContent);

        try
        {
            if (!TryDebit(connection, transaction, "credits", "account_id", commit.BuyerAccountId,
                    plan.CostOf(CashShopCurrencyType.Credits), out _))
                return Failure(CashShopPersistenceFailureReason.InsufficientCredits);
            if (!TryDebit(connection, transaction, "loyalty", "account_id", commit.BuyerAccountId,
                    plan.CostOf(CashShopCurrencyType.Loyalty), out _))
                return Failure(CashShopPersistenceFailureReason.InsufficientLoyalty);

            var coinCost = plan.CostOf(CashShopCurrencyType.Coins);
            var aaPointCost = plan.CostOf(CashShopCurrencyType.AaPoints);
            if (coinCost < 0 || aaPointCost < 0 || commit.LiveMoney < coinCost || commit.LiveAaPoints < aaPointCost)
                return Failure(coinCost > commit.LiveMoney
                    ? CashShopPersistenceFailureReason.InsufficientCoins
                    : CashShopPersistenceFailureReason.InsufficientAaPoints);
            if (!TryWriteLiveWallet(connection, transaction, commit.BuyerCharacterId,
                    commit.LiveMoney - coinCost, commit.LiveAaPoints - aaPointCost))
                return Failure(CashShopPersistenceFailureReason.PersistenceUnavailable);

            var remainingByShop = new Dictionary<uint, int>();
            foreach (var quantity in plan.QuantitiesByShop)
            {
                if (!TryConsumeStock(connection, transaction, quantity.Key, quantity.Value, out var remaining))
                    return Failure(CashShopPersistenceFailureReason.SoldOut, quantity.Key);
                remainingByShop[quantity.Key] = remaining;
            }

            foreach (var line in plan.Lines)
            {
                using var audit = connection.CreateCommand();
                audit.Transaction = transaction;
                audit.CommandText =
                    "INSERT INTO audit_ics_sales " +
                    "(buyer_account,buyer_char,target_account,target_char,sale_date,shop_item_id,sku,sale_cost,sale_currency,description) " +
                    "VALUES (@buyer_account,@buyer_char,@target_account,@target_char,@sale_date,@shop_item_id,@sku,@sale_cost,@sale_currency,@description)";
                Add(audit, "@buyer_account", commit.BuyerAccountId);
                Add(audit, "@buyer_char", commit.BuyerCharacterId);
                Add(audit, "@target_account", commit.TargetAccountId);
                Add(audit, "@target_char", commit.TargetCharacterId);
                Add(audit, "@sale_date", ServerCalendar.AsUtc(commit.SaleDateUtc));
                Add(audit, "@shop_item_id", line.ShopId);
                Add(audit, "@sku", line.SkuId);
                Add(audit, "@sale_cost", line.Cost);
                Add(audit, "@sale_currency", (byte)line.Currency);
                Add(audit, "@description", string.Empty);
                if (audit.ExecuteNonQuery() != 1)
                    return Failure(CashShopPersistenceFailureReason.AuditWrite);
            }

            foreach (var mail in commit.Mails)
            {
                if (!stageMail(mail))
                    return Failure(CashShopPersistenceFailureReason.DeliveryFailed);
            }

            if (!TryReadBalances(connection, transaction, commit.BuyerAccountId, commit.BuyerCharacterId,
                    out var credits, out var loyalty, out var money, out var aaPoints))
                return Failure(CashShopPersistenceFailureReason.PersistenceUnavailable);

            return new CashShopPurchaseStoreResult(true, CashShopPersistenceFailureReason.None,
                credits, loyalty, money, aaPoints, 0,
                new Dictionary<uint, int>(remainingByShop));
        }
        catch (DbException ex)
        {
            var correlation = CashShopLogCorrelation.ForBuyer(commit.BuyerAccountId, commit.BuyerCharacterId);
            Logger.Error(ex, "ICS purchase persistence failed for buyer correlation {0}", correlation);
            return Failure(CashShopPersistenceFailureReason.PersistenceUnavailable);
        }
    }

    private static bool TryDebit(DbConnection connection, DbTransaction transaction, string column,
        string keyColumn, uint id, long amount, out bool affected)
    {
        affected = false;
        if (amount == 0)
            return true;
        if (amount < 0)
            return false;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {ColumnTable(column)} SET {column}={column}-@amount " +
                              $"WHERE {keyColumn}=@id AND {column}>=@amount";
        Add(command, "@amount", amount);
        Add(command, "@id", id);
        affected = command.ExecuteNonQuery() == 1;
        return affected;
    }

    private static bool TryWriteLiveWallet(DbConnection connection, DbTransaction transaction,
        uint characterId, long money, long aaPoints)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE characters SET money=@money, aa_point=@aa_point WHERE id=@id";
        Add(command, "@money", money);
        Add(command, "@aa_point", aaPoints);
        Add(command, "@id", characterId);
        return command.ExecuteNonQuery() == 1;
    }

    private static bool TryConsumeStock(DbConnection connection, DbTransaction transaction, uint shopId,
        long quantity, out int remaining)
    {
        remaining = 0;
        using (var current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = "SELECT remaining FROM ics_shop_items WHERE shop_id=@shop_id";
            Add(current, "@shop_id", shopId);
            var value = current.ExecuteScalar();
            if (value is null || value == DBNull.Value || !int.TryParse(Convert.ToString(value), out remaining))
                return false;
            if (remaining < 0)
                return true;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE ics_shop_items SET remaining=remaining-@quantity " +
                              "WHERE shop_id=@shop_id AND remaining>=@quantity";
        Add(command, "@quantity", quantity);
        Add(command, "@shop_id", shopId);
        if (command.ExecuteNonQuery() != 1)
            return false;

        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT remaining FROM ics_shop_items WHERE shop_id=@shop_id";
        Add(read, "@shop_id", shopId);
        var updated = read.ExecuteScalar();
        if (updated is null || updated == DBNull.Value || !int.TryParse(Convert.ToString(updated), out remaining))
            return false;
        return true;
    }

    private static bool TryReadBalances(DbConnection connection, DbTransaction transaction, uint accountId,
        uint characterId, out long credits, out long loyalty, out long money, out long aaPoints)
    {
        credits = loyalty = money = aaPoints = 0;
        using (var account = connection.CreateCommand())
        {
            account.Transaction = transaction;
            account.CommandText = "SELECT credits,loyalty FROM accounts WHERE account_id=@account_id";
            Add(account, "@account_id", accountId);
            using var reader = account.ExecuteReader();
            if (!reader.Read())
                return false;
            credits = Convert.ToInt64(reader.GetValue(0));
            loyalty = Convert.ToInt64(reader.GetValue(1));
        }

        using var character = connection.CreateCommand();
        character.Transaction = transaction;
        character.CommandText = "SELECT money,aa_point FROM characters WHERE id=@character_id AND deleted=0";
        Add(character, "@character_id", characterId);
        using var characterReader = character.ExecuteReader();
        if (!characterReader.Read())
            return false;
        money = Convert.ToInt64(characterReader.GetValue(0));
        aaPoints = Convert.ToInt64(characterReader.GetValue(1));
        return true;
    }

    private static string ColumnTable(string column) => column switch
    {
        "credits" or "loyalty" => "accounts",
        "money" or "aa_point" => "characters",
        _ => throw new ArgumentOutOfRangeException(nameof(column))
    };

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CashShopPurchaseStoreResult Failure(
        CashShopPersistenceFailureReason reason,
        uint failedShopId = 0) =>
        new(false, reason, 0, 0, 0, 0, failedShopId, new Dictionary<uint, int>());
}

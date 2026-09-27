using System.Data;
using System.Data.Common;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.CashShop;

public enum CashShopAaPointFailureReason
{
    None,
    /// <summary>The request carried no cash, or a cash amount no balance can cover.</summary>
    InvalidAmount,
    /// <summary>The ratio published to the client is not a usable positive value.</summary>
    ExchangeRatioUnavailable,
    /// <summary>The grant for this cash does not fit the wallet.</summary>
    Overflow,
    /// <summary>The character does not hold the cash the request asks to spend.</summary>
    InsufficientCash,
    /// <summary>The journal row could not be staged, so nothing may be charged.</summary>
    AuditWrite,
    PersistenceUnavailable
}

/// <summary>Everything the checkout needs to charge cash, grant points, and journal the sale.</summary>
public sealed record CashShopAaPointPurchaseCommit(
    uint AccountId,
    uint CharacterId,
    DateTime PurchaseDateUtc,
    long CashSpent,
    long AaPoints,
    uint ExchangeRatio);

public sealed record CashShopAaPointPurchaseResult(
    bool Succeeded,
    CashShopAaPointFailureReason Reason,
    long Money,
    long AaPoints,
    long Money2,
    long BankAaPoints)
{
    public ErrorMessageType ClientError => Reason switch
    {
        CashShopAaPointFailureReason.InsufficientCash => ErrorMessageType.NotEnoughCoin,
        _ => ErrorMessageType.IngameShopBuyFailAaPoint
    };
}

/// <summary>
/// Stages the database side of an AA-point checkout on a caller-owned transaction. The caller
/// commits only after the debit, the credit, and the journal row have all been staged.
/// </summary>
public static class CashShopAaPointPurchaseStore
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static CashShopAaPointPurchaseResult Stage(
        DbConnection connection,
        DbTransaction transaction,
        CashShopAaPointPurchaseCommit commit)
    {
        if (connection == null || transaction == null || commit is null)
            return Failure(CashShopAaPointFailureReason.InvalidAmount);

        try
        {
            if (!TryDebitCash(connection, transaction, commit.CharacterId, commit.CashSpent))
                return Failure(CashShopAaPointFailureReason.InsufficientCash);

            if (!TryCreditAaPoints(connection, transaction, commit.CharacterId, commit.AaPoints))
                return Failure(CashShopAaPointFailureReason.PersistenceUnavailable);

            using (var audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText =
                    "INSERT INTO audit_ics_aa_point_purchases " +
                    "(account_id,character_id,purchase_date,cash_spent,aa_points,exchange_ratio) " +
                    "VALUES (@account_id,@character_id,@purchase_date,@cash_spent,@aa_points,@exchange_ratio)";
                Add(audit, "@account_id", commit.AccountId);
                Add(audit, "@character_id", commit.CharacterId);
                Add(audit, "@purchase_date", ServerCalendar.AsUtc(commit.PurchaseDateUtc));
                Add(audit, "@cash_spent", commit.CashSpent);
                Add(audit, "@aa_points", commit.AaPoints);
                Add(audit, "@exchange_ratio", commit.ExchangeRatio);
                if (audit.ExecuteNonQuery() != 1)
                    return Failure(CashShopAaPointFailureReason.AuditWrite);
            }

            if (!TryReadWallet(connection, transaction, commit.CharacterId,
                    out var money, out var aaPoints, out var money2, out var bankAaPoints))
                return Failure(CashShopAaPointFailureReason.PersistenceUnavailable);

            return new CashShopAaPointPurchaseResult(true, CashShopAaPointFailureReason.None,
                money, aaPoints, money2, bankAaPoints);
        }
        catch (DbException ex)
        {
            var correlation = CashShopLogCorrelation.ForBuyer(commit.AccountId, commit.CharacterId);
            Logger.Error(ex, "ICS AA point checkout failed for buyer correlation {0}", correlation);
            return Failure(CashShopAaPointFailureReason.PersistenceUnavailable);
        }
    }


    private static bool TryDebitCash(DbConnection connection, DbTransaction transaction,
        uint characterId, long amount)
    {
        if (amount <= 0)
            return false;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The charge is the delta on the live wallet, never a whole row written back: another
        // spend or a bank transfer may have moved the row since this request was planned.
        // Guarded on the persisted balance so a concurrent spend cannot be overwritten here.
        command.CommandText =
            "UPDATE characters SET money=money-@amount WHERE id=@id AND deleted=0 AND money>=@amount";
        Add(command, "@amount", amount);
        Add(command, "@id", characterId);
        return command.ExecuteNonQuery() == 1;
    }

    private static bool TryCreditAaPoints(DbConnection connection, DbTransaction transaction,
        uint characterId, long amount)
    {
        if (amount <= 0)
            return false;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The ceiling keeps the granted total inside the range the wallet can represent, so a
        // checkout can never leave a balance that a later read or save cannot carry.
        command.CommandText =
            "UPDATE characters SET aa_point=aa_point+@amount " +
            "WHERE id=@id AND deleted=0 AND aa_point>=0 AND aa_point<=@ceiling-@amount";
        Add(command, "@amount", amount);
        Add(command, "@ceiling", CashShopAaPointPurchaseRules.MaxWalletAmount);
        Add(command, "@id", characterId);
        return command.ExecuteNonQuery() == 1;
    }

    private static bool TryReadWallet(DbConnection connection, DbTransaction transaction, uint characterId,
        out long money, out long aaPoints, out long money2, out long bankAaPoints)
    {
        money = aaPoints = money2 = bankAaPoints = 0;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT money,aa_point,money2,bank_aa_point FROM characters WHERE id=@id AND deleted=0";
        Add(command, "@id", characterId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;
        money = Convert.ToInt64(reader.GetValue(0));
        aaPoints = Convert.ToInt64(reader.GetValue(1));
        money2 = Convert.ToInt64(reader.GetValue(2));
        bankAaPoints = Convert.ToInt64(reader.GetValue(3));
        return true;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CashShopAaPointPurchaseResult Failure(CashShopAaPointFailureReason reason) =>
        new(false, reason, 0, 0, 0, 0);
}

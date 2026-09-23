using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Moves the auction's shop-cash escrow and the bid row in one transaction when a database
/// connection is available. Tests supply a stand-in that does not open MySQL.
/// </summary>
internal interface IPlotAuctionWallet
{
    bool TryApply(Character character, long signedAmount, Func<MySqlConnection, MySqlTransaction, bool> persist);

    /// <summary>Credits an account that has no live character, on the same transaction as <paramref name="persist"/>.</summary>
    bool TryCreditAccount(uint accountId, long amount, Func<MySqlConnection, MySqlTransaction, bool> persist);
}

/// <summary>Account credits. A positive amount is a refund; a negative amount is a bid.</summary>
internal sealed class AccountCreditWallet : IPlotAuctionWallet
{
    public bool TryApply(Character character, long signedAmount, Func<MySqlConnection, MySqlTransaction, bool> persist)
    {
        if (character == null)
            return false;
        var applied = Apply(character.AccountId, signedAmount, persist, out var credits, out var loyalty);
        if (!applied || signedAmount == 0)
            return applied;

        var notice = signedAmount > 0 ? (byte)2 : (byte)0;
        character.SendPacket(new SCICSCashPointPacket(credits, loyalty, true, notice));
        return true;
    }

    public bool TryCreditAccount(uint accountId, long amount, Func<MySqlConnection, MySqlTransaction, bool> persist) =>
        amount > 0 && Apply(accountId, amount, persist, out _, out _);

    private static bool Apply(uint accountId, long signedAmount, Func<MySqlConnection, MySqlTransaction, bool> persist, out int credits, out int loyalty)
    {
        credits = 0;
        loyalty = 0;
        if (persist == null || accountId == 0)
            return false;
        if (signedAmount == 0)
        {
            try
            {
                return persist(null, null);
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Plot auction wallet persist failed");
                return false;
            }
        }

        if (signedAmount > int.MaxValue || signedAmount < int.MinValue)
            return false;

        MySqlConnection connection = null;
        MySqlTransaction transaction = null;
        try
        {
            connection = MySQL.CreateConnection();
            transaction = connection.BeginTransaction();
            if (!AccountManager.Instance.AddCreditsOn(accountId, (int)signedAmount, connection, transaction))
            {
                transaction.Rollback();
                return false;
            }

            if (!persist(connection, transaction))
            {
                transaction.Rollback();
                return false;
            }

            transaction.Commit();
            var details = AccountManager.Instance.GetAccountDetails(accountId);
            credits = details.Credits;
            loyalty = details.Loyalty;
            return true;
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Plot auction wallet update failed");
            try
            {
                transaction?.Rollback();
            }
            catch (Exception rollbackEx)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(rollbackEx, "Plot auction wallet rollback failed");
            }

            return false;
        }
        finally
        {
            transaction?.Dispose();
            connection?.Dispose();
        }
    }
}

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
}

/// <summary>Account credits. A positive amount is a refund; a negative amount is a bid.</summary>
internal sealed class AccountCreditWallet : IPlotAuctionWallet
{
    public bool TryApply(Character character, long signedAmount, Func<MySqlConnection, MySqlTransaction, bool> persist)
    {
        if (character == null || persist == null)
            return false;
        if (signedAmount == 0)
            return persist(null, null);
        if (signedAmount > int.MaxValue || signedAmount < int.MinValue)
            return false;

        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        if (!AccountManager.Instance.AddCreditsOn(character.AccountId, (int)signedAmount, connection, transaction))
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
        var details = AccountManager.Instance.GetAccountDetails(character.AccountId);
        var notice = signedAmount > 0 ? (byte)2 : (byte)0;
        character.SendPacket(new SCICSCashPointPacket(details.Credits, 0, true, notice));
        return true;
    }
}

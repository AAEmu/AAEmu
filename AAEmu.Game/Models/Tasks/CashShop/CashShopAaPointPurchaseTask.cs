using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Tasks.CashShop;

/// <summary>
/// Charges wallet cash and grants AA points as one indivisible checkout: the debit, the credit,
/// and the journal row are staged on a single transaction and roll back together, so a failure
/// anywhere leaves neither a charge nor a grant nor a log entry behind.
/// </summary>
public class CashShopAaPointPurchaseTask(uint requestedCash, Character buyer) : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        if (!CashShopManager.Instance.IsOpenForPlayers)
        {
            buyer.SendErrorMessage(ErrorMessageType.Maintenance);
            return;
        }

        // The checkout spends the same wallet as a goods purchase, so it takes the same locks in
        // the same global order; taking them differently is what previously deadlocked a craft
        // against a purchase.
        CashShopPurchaseLocking.Execute(buyer, ExecuteLocked);
    }

    private void ExecuteLocked()
    {
        var ratio = CashShopManager.Instance.AaPointExchangeRatio;
        var account = AccountManager.Instance.GetAccountDetails(buyer.AccountId);

        // The checkout spends the account's cash-shop credits, not the character's gold: the
        // client prices this window in PRICE_TYPE_AA_CASH, which is the credits balance published
        // in SCICSCashPoint. Planning against buyer.Money would price a purchase in copper.
        if (!CashShopAaPointPurchaseRules.TryCreatePlan(
                requestedCash, account.Credits, buyer.AaPoint, ratio,
                out var plan, out var reason))        {
            Refuse(reason);
            return;
        }

        CashShopAaPointPurchaseResult persisted;
        using (var connection = MySQL.CreateConnection())
        using (var transaction = connection.BeginTransaction())
        using (CashShopPurchaseLocking.EnterTransaction())
        {
            try
            {
                var commit = new CashShopAaPointPurchaseCommit(
                    buyer.AccountId,
                    buyer.Id,
                    ServerCalendar.UtcNow,
                    plan.CashSpent,
                    plan.AaPoints,
                    plan.ExchangeRatio,
                    account.Credits,
                    buyer.Money,
                    buyer.Money2,
                    buyer.AaPoint,
                    buyer.BankAaPoint);
                persisted = CashShopAaPointPurchaseStore.Stage(connection, transaction, commit);

                if (!persisted.Succeeded)
                {
                    transaction.Rollback();
                    Refuse(persisted.Reason);
                    return;
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // The original database failure is the useful one.
                }
                var correlation = CashShopLogCorrelation.ForBuyer(buyer.AccountId, buyer.Id);
                Logger.Error(ex, "ICS AA point checkout failed for buyer correlation {0}", correlation);
                buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyFailAaPoint);
                return;
            }
        }

        // The committed values, not the planned deltas, become the in-memory state: the row now
        // holds what the read-back returned, and a delta on the pre-commit wallet would drift from
        // it. The four wallet columns are written whole, so all four are refreshed here.
        account.Credits = (int)persisted.Credits;
        buyer.Money = persisted.Money;
        buyer.Money2 = persisted.Money2;
        buyer.AaPoint = persisted.AaPoints;
        buyer.BankAaPoint = persisted.BankAaPoints;

        buyer.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy,
            [
                new AAPointUpdate(plan.AaPoints)
            ], []));

        // The charge window never asks for a refresh after buying, so the new credits balance is
        // published here or the shop keeps showing the pre-purchase figure.
        buyer.SendPacket(new SCICSCashPointPacket(account.Credits));

        var logCorrelation = CashShopLogCorrelation.ForBuyer(buyer.AccountId, buyer.Id);
        Logger.Info("ICSBuyAAPoint buyer={0} credits={1} ratio={2} aaPoints={3}",
            logCorrelation, plan.CashSpent, plan.ExchangeRatio, plan.AaPoints);
    }

    private void Refuse(CashShopAaPointFailureReason reason)
    {
        buyer.SendErrorMessage(reason switch
        {
            CashShopAaPointFailureReason.InsufficientCash => ErrorMessageType.NotEnoughCoin,
            _ => ErrorMessageType.IngameShopBuyFailAaPoint
        });

        // The client prices this window against the credits balance it was last told about, so a
        // refused purchase has to correct it or the next attempt is priced from a stale figure.
        var account = AccountManager.Instance.GetAccountDetails(buyer.AccountId);
        buyer.SendPacket(new SCICSCashPointPacket(account.Credits));
    }
}

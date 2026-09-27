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
        if (!CashShopAaPointPurchaseRules.TryCreatePlan(
                requestedCash, buyer.Money, buyer.AaPoint, ratio,
                out var plan, out var reason))
        {
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
                    plan.ExchangeRatio);
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

        // Only the committed deltas are applied in memory, so the value the player sees is the
        // value the row now holds. The full read-back is not copied over the fields this
        // checkout did not touch.
        buyer.Money -= plan.CashSpent;
        buyer.AaPoint += plan.AaPoints;
        buyer.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy,
            [
                new MoneyChange(-plan.CashSpent),
                new AAPointUpdate(plan.AaPoints)
            ], []));

        var logCorrelation = CashShopLogCorrelation.ForBuyer(buyer.AccountId, buyer.Id);
        Logger.Info("ICSBuyAAPoint buyer={0} cash={1} ratio={2} aaPoints={3}",
            logCorrelation, plan.CashSpent, plan.ExchangeRatio, plan.AaPoints);
    }

    private void Refuse(CashShopAaPointFailureReason reason)
    {
        buyer.SendErrorMessage(reason switch
        {
            CashShopAaPointFailureReason.InsufficientCash => ErrorMessageType.NotEnoughCoin,
            _ => ErrorMessageType.IngameShopBuyFailAaPoint
        });
    }
}

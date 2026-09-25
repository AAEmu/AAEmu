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

public class CashShopBuyTask(byte buyMode, Character buyer, Character targetPlayer, List<IcsPurchase> shoppingCart)
    : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        if (!CashShopManager.Instance.IsOpenForPlayers)
        {
            FailBuy(ErrorMessageType.Maintenance);
            return;
        }

        // The persistence gate, the character state lock, the account lock and the purchase lock are taken in
        // the one global order (CashShopPurchaseLocking.DocumentedOrder). Taking the gate last is what deadlocked
        // this task against a craft or a save: the craft holds the gate and waits for the state lock while the
        // purchase held that lock and waited for the gate.
        CashShopPurchaseLocking.Execute(buyer, ExecuteLocked);
    }

    private void ExecuteLocked()
    {
        var account = AccountManager.Instance.GetAccountDetails(buyer.AccountId);
        var context = new CashShopPurchaseContext(
            account.Credits,
            account.Loyalty,
            buyer.Money,
            buyer.AaPoint,
            buyer.Level,
            targetPlayer.Id != buyer.Id,
            ServerCalendar.UtcNow,
            questId => buyer.Quests.HasQuestCompleted(questId),
            shopId => CashShopManager.Instance.GetPurchasedItemCount(
                buyer.AccountId,
                buyer.Id,
                CashShopManager.Instance.ShopItems[shopId]));

        if (!CashShopPurchaseRules.TryCreatePlan(
                shoppingCart,
                CashShopManager.Instance.ShopItems,
                context,
                out var plan,
                out var failure))
        {
            FailBuy(failure);
            return;
        }

        CashShopMailDelivery delivery;
        try
        {
            delivery = CashShopMailDeliveryFactory.Create(
                plan,
                buyer,
                targetPlayer,
                ItemManager.Instance,
                MailManager.Instance);
        }
        catch (Exception ex)
        {
            var correlation = CashShopLogCorrelation.ForBuyer(buyer.AccountId, buyer.Id);
            Logger.Error(ex, "ICS purchase delivery could not be created for buyer correlation {0}", correlation);
            FailBuy(new CashShopPurchaseFailure(
                CashShopPurchaseFailureReason.InvalidContent,
                plan.Lines[0].ShopId));
            return;
        }

        // Nests inside the scope CashShopPurchaseLocking.Execute opened before any lock: the mail rows and the
        // wallet row below are one snapshot, and the gate is already held for both.
        using (delivery)
        using (MailManager.Instance.DeferPersist())
        {
            CashShopPurchaseStoreResult persisted;
            using (var connection = MySQL.CreateConnection())
            using (var transaction = connection.BeginTransaction())
            using (CashShopPurchaseLocking.EnterTransaction())
            {
                try
                {
                    var commit = new CashShopPurchaseCommit(
                        buyer.AccountId,
                        buyer.Id,
                        targetPlayer.AccountId,
                        targetPlayer.Id,
                        ServerCalendar.UtcNow,
                        plan,
                        delivery.Mails,
                        buyer.Money,
                        buyer.AaPoint,
                        buyer.Money2,
                        buyer.BankAaPoint);
                    persisted = CashShopPurchaseStore.Stage(
                        connection,
                        transaction,
                        commit,
                        mail => MailManager.Instance.TryDeliverOn(mail, connection, transaction));

                    if (!persisted.Succeeded)
                    {
                        transaction.Rollback();
                        FailBuy(StoreFailure(persisted));
                        return;
                    }

                    transaction.Commit();
                    delivery.CompleteCommit(() =>
                    {
                        ApplyCommittedBuyerState(plan, persisted);
                        CashShopManager.Instance.ApplyCommittedStock(persisted.RemainingByShop);
                    });
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
                    Logger.Error(ex, "ICS purchase persistence failed for buyer correlation {0}", correlation);
                    FailBuy(new CashShopPurchaseFailure(
                        CashShopPurchaseFailureReason.InvalidContent,
                        plan.Lines[0].ShopId));
                    return;
                }
            }

            foreach (var (shopId, remaining) in persisted.RemainingByShop)
                buyer.SendPacket(new SCICSSyncGoodPacket((int)shopId, remaining));

            buyer.SendPacket(new SCICSBuySucceededPacket(
                buyMode,
                SCICSBuySucceededPacket.ReceiveWayChargedMail,
                targetPlayer.Name,
                checked((int)plan.CostOf(CashShopCurrencyType.AaPoints)),
                plan.Lines.Select(line => (line.ShopId, line.DetailIndex)).ToArray()));
            CashShopManager.Instance.SendBuyCounts(buyer.Connection, buyer.AccountId, buyer.Id);

            foreach (var line in plan.Lines)
            {
                var buyerCorrelation = CashShopLogCorrelation.ForBuyer(buyer.AccountId, buyer.Id);
                var targetCorrelation = CashShopLogCorrelation.ForBuyer(targetPlayer.AccountId, targetPlayer.Id);
                Logger.Info("ICSBuyGood buyer={0} target={1} - {2} x {3}, SKU:{4}",
                    buyerCorrelation, targetCorrelation, line.DeliveryTitle, line.ItemCount, line.SkuId);
            }

            // The purchase transaction made the wallet row durable. Flush the rest of the buyer's row now, while
            // the gate scope is still open, so a World that dies before the next autosave cannot resurrect the
            // pre-purchase state of anything the delivery touched.
            MailManager.Instance.PersistNow();
        }
    }

    private void ApplyCommittedBuyerState(CashShopPurchasePlan plan, CashShopPurchaseStoreResult persisted)
    {
        var coinCost = plan.CostOf(CashShopCurrencyType.Coins);
        var aaPointCost = plan.CostOf(CashShopCurrencyType.AaPoints);
        buyer.Money -= coinCost;
        buyer.AaPoint -= aaPointCost;
        buyer.BmPoint = checked((int)persisted.Loyalty);

        var walletTasks = new List<ItemTask>();
        if (coinCost > 0)
            walletTasks.Add(new MoneyChange(-coinCost));
        if (aaPointCost > 0)
            walletTasks.Add(new AAPointUpdate(-aaPointCost));
        if (walletTasks.Count > 0)
            buyer.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy, walletTasks, []));

        buyer.SendPacket(new SCICSCashPointPacket(checked((int)persisted.Credits)));
        buyer.SendPacket(new SCBmPointPacket(checked((int)persisted.Loyalty)));
    }

    private static CashShopPurchaseFailure StoreFailure(CashShopPurchaseStoreResult persisted) =>
        persisted.ClientFailure;

    /// <summary>Sends a failure reply and an optional separate chat notification.</summary>
    private void FailBuy(ErrorMessageType wireError, ErrorMessageType? toast = null, uint shopId = 0) =>
        FailBuy(new CashShopPurchaseFailure(
            CashShopPurchaseFailureReason.InvalidContent,
            shopId,
            wireError,
            toast ?? wireError));

    private void FailBuy(CashShopPurchaseFailure failure)
    {
        buyer.SendErrorMessage(failure.Toast);
        IReadOnlyList<(uint CashShopId, ErrorMessageType Reason)> itemFailures =
            failure.ShopId == 0 ? [] : [(failure.ShopId, failure.WireError)];
        buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, failure.WireError, itemFailures));
        CashShopManager.Instance.SendBuyCounts(buyer.Connection, buyer.AccountId, buyer.Id);
    }
}

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Tasks.TimedRewards;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// For timed adding credits and loyalty
/// </summary>
public class TimedRewardsManager(ITaskManager taskManager) : Singleton<TimedRewardsManager>, ITimedRewardsManager
{
    private const short MaxLabor = 2000;
    private const short MaxLaborPremium = 5000;

    public void Initialize()
    {
        taskManager.Schedule(new TimedRewardsTask(), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public static short GetMaxLabor(bool isPremium)
    {
        return isPremium ? MaxLaborPremium : MaxLabor;
    }

    /// <summary>
    /// Adds labor, internal use only
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="currentLabor"></param>
    /// <param name="addLabor"></param>
    private void DoAddLabor(GameConnection connection, short currentLabor, int addLabor)
    {
        var maxLaborToAdd = GetMaxLabor(connection.Payment.PremiumState) - currentLabor;
        if (maxLaborToAdd < 0)
            maxLaborToAdd = 0;
        addLabor = Math.Min(addLabor, maxLaborToAdd);
        AccountManager.Instance.UpdateTickTimes(connection.AccountId, DateTime.UtcNow, true, false, false);
        if (addLabor > 0)
        {
            var newLabor = (short)(currentLabor + addLabor);
            AccountManager.Instance.UpdateLabor(connection.AccountId, newLabor);

            var activeChar = connection.ActiveChar;
            activeChar?.SendPacket(new SCCharacterLaborPowerChangedPacket(addLabor, 0, 0, 0, 0, 0));

            // Update cache if character was logged in. Only the account pool changed here - carry the
            // local pool over unchanged, it is account-wide state too and this tick does not touch it.
            activeChar?.InitializeLaborCache(newLabor, activeChar.LocalLaborPower, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Adds to the SERVER-LOCAL pool ("Online Labor"), the one that fills while the player is logged
    /// in. <see cref="Character.AddLocalLaborPower"/> clamps to the premium grade's max_local_labor and
    /// emits the change in the packet's localAmount field, which is the counter the client adds it to.
    /// Only runs with a character in the world - there is nothing to regenerate at character select.
    /// </summary>
    private static void DoAddLocalLabor(GameConnection connection, int addLabor)
    {
        if (addLabor <= 0)
            return;

        // Without a character in the world there is nothing to credit. Leave the tick time alone in
        // that case, otherwise sitting at character select silently eats one tick after another.
        var activeChar = connection.ActiveChar;
        if (activeChar == null)
            return;

        AccountManager.Instance.UpdateTickTimes(connection.AccountId, DateTime.UtcNow, true, false, false);
        activeChar.AddLocalLaborPower(addLabor);
    }

    public void DoTick()
    {
        var connections = GameConnectionTable.Instance.GetConnections();
        foreach (var connection in connections)
        {
            //var character = connection.ActiveChar;
            // Grab current values for last ticks
            var accountDetails = AccountManager.Instance.GetAccountDetails(connection.AccountId);

            // Online regeneration fills the SERVER-LOCAL pool, which the client labels "Online Labor" -
            // its own ui_texts describe that one as restoring while you are logged in, and the account
            // pool as restoring only while you are logged off. Crediting the account pool here is why
            // "Online Labor" stayed at 0 forever while "Offline Labor" kept climbing.
            if (AppConfiguration.Instance.Labor.TickMinutes > 0 && accountDetails.LastLaborTick.AddMinutes(AppConfiguration.Instance.Labor.TickMinutes) <= DateTime.UtcNow)
            {
                var addLabor = AppConfiguration.Instance.Labor.GetTickAmount(connection.Payment.PremiumState);
                DoAddLocalLabor(connection, addLabor);
            }

            // Distribute Credits if needed. 10.0.2.13 does not push the account balance on a timer (a live
            // official capture sends neither the credits nor loyalty packet at world entry), and the legacy
            // SCICSCashPoint opcode 0x1D6 now collides with the client's SCMatchingInvitationInfo — sending it
            // crashes the client on deserialization. The balance is still accrued; the client reads it on demand.
            if (AppConfiguration.Instance.Credits.TickMinutes > 0 && accountDetails.LastCreditsTick.AddMinutes(AppConfiguration.Instance.Credits.TickMinutes) <= DateTime.UtcNow)
            {
                // Update Credits
                AccountManager.Instance.AddCredits(connection.AccountId, AppConfiguration.Instance.Credits.GetTickAmount(connection.Payment.PremiumState));
                AccountManager.Instance.UpdateTickTimes(connection.AccountId, DateTime.UtcNow, false, true, false);
            }

            // Distribute Loyalty if needed
            if (AppConfiguration.Instance.Loyalty.TickMinutes > 0 && accountDetails.LastLoyaltyTick.AddMinutes(AppConfiguration.Instance.Loyalty.TickMinutes) <= DateTime.UtcNow)
            {
                // Update Loyalty
                AccountManager.Instance.AddLoyalty(connection.AccountId, AppConfiguration.Instance.Loyalty.GetTickAmount(connection.Payment.PremiumState));
                AccountManager.Instance.UpdateTickTimes(connection.AccountId, DateTime.UtcNow, false, false, true);
            }
        }
    }

    public void DoDailyAccountLogin(uint accountId)
    {
        if (AppConfiguration.Instance.Credits.DailyLogin > 0)
            AccountManager.Instance.AddCredits(accountId, AppConfiguration.Instance.Credits.DailyLogin);

        if (AppConfiguration.Instance.Loyalty.DailyLogin > 0)
            AccountManager.Instance.AddLoyalty(accountId, AppConfiguration.Instance.Loyalty.DailyLogin);

        AccountManager.Instance.UpdateDivineClock(accountId, 0, 0);
    }

    public void AddOfflineLabor(GameConnection connection, DateTime lastLoginTime, short currentLabor)
    {
        var delta = DateTime.UtcNow - lastLoginTime;
        var ticksToAdd = (int)Math.Floor(delta.TotalMinutes / AppConfiguration.Instance.LaborOffline.TickMinutes);
        if (ticksToAdd <= 0)
            return;
        var addLabor = AppConfiguration.Instance.LaborOffline.GetTickAmount(connection.Payment.PremiumState) * ticksToAdd;
        DoAddLabor(connection, currentLabor, addLabor);
    }
}

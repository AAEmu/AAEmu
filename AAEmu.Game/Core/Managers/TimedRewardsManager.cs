using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Tasks.TimedRewards;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// For timed adding credits and loyalty
/// </summary>
public class TimedRewardsManager : Singleton<TimedRewardsManager>
{
    public static short MaxLabor = 2000;
    public static short MaxLaborPremium = 5000;

    public void Initialize()
    {
        TaskManager.Instance.Schedule(new TimedRewardsTask(), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
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

            connection.ActiveChar?.SendPacket(new SCCharacterLaborPowerChangedPacket(addLabor, 0, 0, 0));

            // Update cache if character was logged in
            connection.ActiveChar?.InitializeLaborCache(newLabor, DateTime.UtcNow);
        }
    }

    public void DoTick()
    {
        if ((AppConfiguration.Instance.Credits.TickMinutes <= 0) && (AppConfiguration.Instance.Loyalty.TickMinutes <= 0))
            return;

        var connections = GameConnectionTable.Instance.GetConnections();
        foreach (var connection in connections)
        {
            //var character = connection.ActiveChar;
            // Grab current values for last ticks
            var accountDetails = AccountManager.Instance.GetAccountDetails(connection.AccountId);

            // Distribute Labor if needed (only for online labor)
            if (AppConfiguration.Instance.Labor.TickMinutes > 0 && accountDetails.LastLaborTick.AddMinutes(AppConfiguration.Instance.Labor.TickMinutes) <= DateTime.UtcNow)
            {
                var addLabor = AppConfiguration.Instance.Labor.GetTickAmount(connection.Payment.PremiumState);
                DoAddLabor(connection, accountDetails.Labor, addLabor);
            }

            // Distribute Credits if needed
            if (AppConfiguration.Instance.Credits.TickMinutes > 0 && accountDetails.LastCreditsTick.AddMinutes(AppConfiguration.Instance.Credits.TickMinutes) <= DateTime.UtcNow)
            {
                // Update Credits
                AccountManager.Instance.AddCredits(connection.AccountId, AppConfiguration.Instance.Credits.GetTickAmount(connection.Payment.PremiumState));
                AccountManager.Instance.UpdateTickTimes(connection.AccountId, DateTime.UtcNow, false, true, false);
                connection.ActiveChar?.SendPacket(new SCICSCashPointPacket(AccountManager.Instance.GetAccountDetails(connection.AccountId).Credits));
            }

            // Distribute Loyalty if needed
            if (AppConfiguration.Instance.Loyalty.TickMinutes > 0 && accountDetails.LastLoyaltyTick.AddMinutes(AppConfiguration.Instance.Loyalty.TickMinutes) <= DateTime.UtcNow)
            {
                // Update Loyalty
                AccountManager.Instance.AddLoyalty(connection.AccountId, AppConfiguration.Instance.Loyalty.GetTickAmount(connection.Payment.PremiumState));
                AccountManager.Instance.UpdateTickTimes(connection.AccountId, DateTime.UtcNow, false, false, true);
                connection.ActiveChar?.SendPacket(new SCBmPointPacket(AccountManager.Instance.GetAccountDetails(connection.AccountId).Loyalty));
            }
        }
    }

    public void DoDailyAccountLogin(ulong accountId)
    {
        if (AppConfiguration.Instance.Credits.DailyLogin > 0)
            AccountManager.Instance.AddCredits(accountId, AppConfiguration.Instance.Credits.DailyLogin);

        if (AppConfiguration.Instance.Loyalty.DailyLogin > 0)
            AccountManager.Instance.AddLoyalty(accountId, AppConfiguration.Instance.Loyalty.DailyLogin);

        DoDailyAccountDivineClockLogin(accountId);
    }

    public void DoDailyAccountDivineClockLogin(ulong accountId)
    {
        // TODO add initialization of ScheduleItem items for Divine Clock
        // delete existing entries
        AccountManager.Instance.DeleteDivineClockEntries(accountId);
        // create new entries
        var li = GameScheduleManager.Instance.GetMatchingPeriods();
        if (li == null) { return; }

        foreach (var scheduleItemId in li)
        {
            var si = new ScheduleItem
            {
                ScheduleItemId = scheduleItemId,
                Gave = 0,
                Cumulated = 0,
                Updated = DateTime.UtcNow
            };
            // Update Account Divine Clock time
            AccountManager.Instance.UpdateDivineClock(accountId, si.ScheduleItemId, si.Cumulated, si.Gave);
        }
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

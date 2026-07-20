using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Account;

namespace AAEmu.Game.Core.Managers;

public interface IAccountManager : IInitializable
{
    void Add(GameConnection connection);
    void Remove(ulong id);
    bool Contains(ulong id);
    int Count();
    AccountDetails GetAccountDetailsInternal(ulong accountId);
    AccountDetails GetAccountDetails(ulong accountId);
    bool AddCredits(ulong accountId, int creditsAmount);
    bool RemoveCredits(ulong accountId, int credits);
    bool AddLoyalty(ulong accountId, int loyaltyAmount);
    void UpdateLabor(ulong accountId, short laborPower);
    DateTime UpdateLoginTime(ulong accountId, DateTime newTime);
    void UpdateTickTimes(ulong accountId, DateTime newTime, bool updateLabor, bool updateCredits, bool updateLoyalty);
    void UpdateDivineClock(ulong accountId, uint timeElapsed, uint timesTaken);
    (uint, uint) GetDivineClock(ulong accountId);
}

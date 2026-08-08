using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Account;

namespace AAEmu.Game.Core.Managers;

public interface IAccountManager : IInitializable
{
    void Add(GameConnection connection);
    void Remove(uint id);
    bool Contains(uint id);
    int Count();
    AccountDetails GetAccountDetails(uint accountId);
    bool AddCredits(uint accountId, int creditsAmount);
    bool RemoveCredits(uint accountId, int credits);
    bool AddLoyalty(uint accountId, int loyaltyAmount);
    void UpdateLabor(uint accountId, short laborPower);
    void UpdateLocalLabor(uint accountId, int localLabor);
    DateTime UpdateLoginTime(uint accountId, DateTime newTime);
    void UpdateTickTimes(uint accountId, DateTime newTime, bool updateLabor, bool updateCredits, bool updateLoyalty);
    void UpdateDivineClock(uint accountId, uint timeElapsed, uint timesTaken);
    (uint, uint) GetDivineClock(uint accountId);
}

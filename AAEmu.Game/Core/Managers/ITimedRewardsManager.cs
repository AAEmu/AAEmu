using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Managers;

public interface ITimedRewardsManager : IInitializable
{
    void DoTick();
    void DoDailyAccountLogin(ulong accountId);
    void AddOfflineLabor(GameConnection connection, DateTime lastLoginTime, short currentLabor);
}

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IAccountAttributeManager : ILoadable
{
    List<AccountAttribute> Get(uint accountId, uint worldId);
    AccountAttribute Find(uint accountId, uint kindId, uint kindValue, uint worldId);
    AccountAttribute Change(uint accountId, uint kindId, uint kindValue, uint worldId, bool isAdd, int count, int durationMinutes);
    (int, int) Save(MySqlConnection connection, MySqlTransaction transaction);
}

using AAEmu.Commons.Utils.DB;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionPersistenceConnectionFactory
{
    MySqlConnection Open();
}

public sealed class MySqlExpeditionPersistenceConnectionFactory : IExpeditionPersistenceConnectionFactory
{
    public MySqlConnection Open() => MySQL.CreateConnection();
}

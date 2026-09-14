using AAEmu.Commons.Utils.DB;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionActivityConnectionFactory
{
    MySqlConnection Open();
}

public sealed class MySqlExpeditionActivityConnectionFactory : IExpeditionActivityConnectionFactory
{
    public MySqlConnection Open() => MySQL.CreateConnection();
}

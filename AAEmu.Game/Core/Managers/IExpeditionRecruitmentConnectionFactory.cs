using AAEmu.Commons.Utils.DB;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionRecruitmentConnectionFactory
{
    MySqlConnection Open();
}

public sealed class MySqlExpeditionRecruitmentConnectionFactory : IExpeditionRecruitmentConnectionFactory
{
    public MySqlConnection Open() => MySQL.CreateConnection();
}

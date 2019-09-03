using AAEmu.Commons.Utils;

namespace AAEmu.Login.Models
{
    public class AppConfiguration : Singleton<AppConfiguration>
    {
        public string SecretKey { get; set; }
        public DBConnections Connections { get; set; }
        public NetworkConfig InternalNetwork { get; set; }
        public NetworkConfig Network { get; set; }

        public class NetworkConfig
        {
            public string Host { get; set; }
            public ushort Port { get; set; }
            public int NumConnections { get; set; }
        }

        public class DBConnections
        {
            public MySqlConnectionSettings MySQLProvider { get; set; }
        }

        public class MySqlConnectionSettings
        {
            public string Host { get; set; }
            public ushort Port { get; set; }
            public string User { get; set; }
            public string Password { get; set; }
            public string Database { get; set; }
        }
    }
}

using AAEmu.Commons.Models;
using AAEmu.Commons.Utils;

namespace AAEmu.Login.Models;

public class AppConfiguration : Singleton<AppConfiguration>
{
    public required string SecretKey { get; set; }
    public bool AutoAccount { get; set; }
    public bool SkipHostResolve { get; set; }
    public required DBConnections Connections { get; set; }
    public required NetworkConfig InternalNetwork { get; set; }
    public required NetworkConfig Network { get; set; }

    public class NetworkConfig
    {
        public required string Host { get; set; }
        public required ushort Port { get; set; }
        public required int NumConnections { get; set; }
    }

    public class DBConnections
    {
        public required MySqlConnectionSettings MySQLProvider { get; set; }
    }
}

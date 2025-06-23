using System.ComponentModel.DataAnnotations;
using AAEmu.Commons.Models;

namespace AAEmu.Login.Models;

public class AppConfiguration
{
    [Required]
    public required string SecretKey { get; set; }
    public bool AutoAccount { get; set; }
    public bool SkipHostResolve { get; set; }
    [Required]
    public required DBConnections Connections { get; set; }
    [Required]
    public required NetworkConfig InternalNetwork { get; set; }
    [Required]
    public required NetworkConfig Network { get; set; }

    public class NetworkConfig
    {
        [Required]
        public required string Host { get; set; }
        [Required]
        public required ushort Port { get; set; }
        public required int NumConnections { get; set; }
    }

    public class DBConnections
    {
        [Required]
        public required MySqlConnectionSettings MySQLProvider { get; set; }
    }
}

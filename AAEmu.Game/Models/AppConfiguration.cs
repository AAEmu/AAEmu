using System.Collections.Generic;
using AAEmu.Commons.Models;
using AAEmu.Commons.Utils;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Models;

public partial class AppConfiguration : Singleton<AppConfiguration>
{
    public byte Id { get; set; }
    public byte[] AdditionalesId { get; set; } = System.Array.Empty<byte>();
    public string SecretKey { get; set; }
    public DBConnections Connections { get; set; }
    public NetworkConfig Network { get; set; }
    public NetworkConfig StreamNetwork { get; set; }
    public NetworkConfig LoginNetwork { get; set; }
    public NetworkConfig WebApiNetwork { get; set; }
    public string CharacterNameRegex { get; set; }
    public int MaxConcurencyThreadPool { get; set; }
    public bool HeightMapsEnable { get; set; }
    public string DiscordToken { get; set; }
    public ExpeditionConfig Expedition { get; set; }
    public WorldConfig World { get; set; }
    public Dictionary<string, int> AccessLevel { get; set; } = [];
    public AccountConfig Account { get; set; }
    public CurrencyValuesConfig Labor { get; set; }
    public CurrencyValuesConfig LaborOffline { get; set; }
    public CurrencyValuesConfig Credits { get; set; }
    public CurrencyValuesConfig Loyalty { get; set; }
    public ClientDataConfig ClientData { get; set; } = new ClientDataConfig();
    public SpecialtyConfig Specialty { get; set; } = new SpecialtyConfig();
    public ScriptsConfig Scripts { get; set; } = new ScriptsConfig();
    public string DefaultLanguage { get; set; } = "en_us";
    public bool DebugInfo { get; set; } = true;
    public uint DebugInfoLevel { get; set; } = 100;

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
}

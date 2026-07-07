using AAEmu.Commons.Models;
using AAEmu.Commons.Utils;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Expeditions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AAEmu.Game.Models;

public partial class AppConfiguration
{
    private static readonly AppConfiguration s_default = new();

    public static AppConfiguration Instance =>
        SingletonContainer.ServiceProvider?.GetService<IOptions<AppConfiguration>>()?.Value
        ?? s_default;

    public byte Id { get; set; }
    public byte[] AdditionalesId { get; set; } = [];
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
    public DungeonsConfig Dungeons { get; set; }
    public Dictionary<string, int> AccessLevel { get; set; } = [];
    public AccountConfig Account { get; set; }
    public CurrencyValuesConfig Labor { get; set; }
    public CurrencyValuesConfig LaborOffline { get; set; }
    public CurrencyValuesConfig Credits { get; set; }
    public CurrencyValuesConfig Loyalty { get; set; }
    public ClientDataConfig ClientData { get; set; } = new();
    public SpecialtyConfig Specialty { get; set; } = new();
    public ScriptsConfig Scripts { get; set; } = new();
    public JusticeConfig Justice { get; set; } = new();
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

        /// <summary>
        /// Gets or sets whether to automatically apply database schema updates without an interactive prompt.
        /// Intended for unattended environments such as Aspire or CI. Not recommended for production use, as it may apply
        /// updates without proper review.
        /// </summary>
        public bool AutoApplyUpdates { get; set; }
    }
}

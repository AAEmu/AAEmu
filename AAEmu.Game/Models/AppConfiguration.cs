﻿using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Models
{
    public class AppConfiguration : Singleton<AppConfiguration>
    {
        public byte Id { get; set; }
        public byte[] AdditionalesId { get; set; } = new byte[0];
        public string SecretKey { get; set; }
        public DBConnections Connections { get; set; }
        public NetworkConfig Network { get; set; }
        public NetworkConfig StreamNetwork { get; set; }
        public NetworkConfig LoginNetwork { get; set; }
        public string CharacterNameRegex { get; set; }
        public int MaxConcurencyThreadPool { get; set; }
        public bool HeightMapsEnable { get; set; }
        public string DiscordToken { get; set; }
        public ExpeditionConfig Expedition { get; set; }
        public WorldConfig World { get; set; }
        public Dictionary<string, int> AccessLevel { get; set; } = new Dictionary<string, int>();
        public AccountConfig Account { get; set; }
        public string ClientDirectory { get; set; }
        public string ClientGamePak { get; set; }

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

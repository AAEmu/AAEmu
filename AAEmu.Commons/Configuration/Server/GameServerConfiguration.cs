﻿using System.Collections.Generic;

 namespace AAEmu.Commons.Configuration.Server
{
    public class GameServerConfiguration
    {
        public uint Id { get; set; }
        public List<uint> AdditionalIds { get; set; }
        public string SecretKey { get; set; } = "test";
        public NetworkConfig Network { get; set; }
        public NetworkConfig StreamNetwork { get; set; }
        public ConnectionStrings ConnectionStrings { get; set; }

        public GameServerConfiguration()
        {
            Network = new NetworkConfig("127.0.0.1", 1239);
            StreamNetwork = new NetworkConfig("127.0.0.1", 1250);
            ConnectionStrings = new ConnectionStrings
            {
                SqliteConnection = "DataSource=Data/compact.sqlite3",
                PostgresConnection = "Host=localhost;Port=5432;Database=Game;Username=postgres;Password=postgres",
                RedisConnection = "127.0.0.1:6379"
            };
        }
    }
}

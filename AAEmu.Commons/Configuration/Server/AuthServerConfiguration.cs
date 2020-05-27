﻿namespace AAEmu.Commons.Configuration.Server
{
    public class AuthServerConfiguration
    {
        public string SecretKey { get; set; } = "test";
        public NetworkConfig Network { get; set; }
        public ConnectionStrings ConnectionStrings { get; set; }

        public AuthServerConfiguration()
        {
            Network = new NetworkConfig("127.0.0.1", 1237);
            ConnectionStrings = new ConnectionStrings
            {
                PostgresConnection = "Host=localhost;Port=5432;Database=Auth;Username=postgres;Password=postgres",
                RedisConnection = "127.0.0.1:6379"
            };
        }
    }
}

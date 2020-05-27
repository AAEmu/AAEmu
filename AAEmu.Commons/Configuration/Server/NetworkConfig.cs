﻿namespace AAEmu.Commons.Configuration.Server
{
    public class NetworkConfig
    {
        public string Host { get; set; } = "127.0.0.1";
        public uint Port { get; set; }

        public NetworkConfig(string host, uint port)
        {
            Host = host;
            Port = port;
        }
    }
}

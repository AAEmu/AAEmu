using System;
using System.Collections.Generic;

namespace AAEmu.DB.Login
{
    public partial class GameServers
    {
        public byte Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public byte Hidden { get; set; }
    }
}

using System.Collections.Generic;
using AAEmu.Commons.Network;
using System;

namespace AAEmu.Game.Models.Game
{
    public class Configurations : PacketMarshaler
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    public class WorldConfig
    {
        public string MOTD { get; set; } = "";
        public string LogoutMessage { get; set; } = "";
        public double AutoSaveInterval { get; set; } = 5.0;
        public double ExpRate { get; set; } = 1.0;
        public double HonorRate { get; set; } = 1.0;
        public double VocationRate { get; set; } = 1.0;
        public double LootRate { get; set; } = 1.0;
    }

}

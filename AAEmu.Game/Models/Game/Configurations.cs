using System.Collections.Generic;
using AAEmu.Commons.Network;
using System;
using AAEmu.Commons.Network.Core;

namespace AAEmu.Game.Models.Game
{
    public class Configurations : PacketMarshaler
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

}

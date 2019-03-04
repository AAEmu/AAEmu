﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInteractNPCEndPacket : GamePacket
    {
        public CSInteractNPCEndPacket() : base(0x066, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            
            _log.Debug("InteractNPCEnd, BcId: {0}", objId);
        }
    }
}

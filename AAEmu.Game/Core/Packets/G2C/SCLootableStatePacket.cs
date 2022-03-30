﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLootableStatePacket : GamePacket
    {
        private readonly uint _iId;
        private readonly bool _isLootable;
        
        public SCLootableStatePacket(uint unitId, bool isLootable) : base(SCOffsets.SCLootableStatePacket, 5)
        {
            _iId = unitId;
            _isLootable = isLootable;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(((ulong)_iId<<32)+0x10000);    
            stream.Write(_isLootable);
            return stream;
        }


    }
}

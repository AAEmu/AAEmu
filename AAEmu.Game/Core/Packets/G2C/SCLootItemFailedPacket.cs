﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Error;

namespace AAEmu.Game.Core.Packets.G2C
{
    class SCLootItemFailedPacket : GamePacket
    {
        private readonly int _errorMessage;
        private readonly ulong _iId;
        private readonly uint _id;

        public SCLootItemFailedPacket(ErrorMessageType errorMessage, ulong iId, uint id) : base(SCOffsets.SCLootItemFailedPacket, 5)
        {
            _errorMessage = (int)errorMessage;
            _iId = iId;
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_errorMessage);
            stream.Write(_iId);
            stream.Write(_id);
            return stream;
        }
    }
}

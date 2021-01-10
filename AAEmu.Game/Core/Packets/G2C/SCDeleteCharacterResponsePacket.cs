using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDeleteCharacterResponsePacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly byte _status;
        private readonly DateTime _deleteRequestedTime;
        private readonly DateTime _deleteDelay;

        public SCDeleteCharacterResponsePacket(uint characterId, byte status, DateTime? deleteRequestedTime = null, DateTime? deleteDelay = null) 
            : base(SCOffsets.SCDeleteCharacterResponsePacket, 5)
        {
            _characterId = characterId;
            _status = status;
            _deleteRequestedTime = deleteRequestedTime ?? DateTime.MinValue;
            _deleteDelay = deleteDelay ?? DateTime.MinValue;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_status);
            stream.Write(_deleteRequestedTime);
            stream.Write(_deleteDelay);
            return stream;
        }
    }
}

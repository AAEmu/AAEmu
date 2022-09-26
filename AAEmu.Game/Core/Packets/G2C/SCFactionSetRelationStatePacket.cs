using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionSetRelationStatePacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _id2;
        private readonly byte _state;
        private readonly DateTime _expireTime;
        private readonly byte _nextState;
        
        public SCFactionSetRelationStatePacket(uint id, uint id2, byte state, DateTime expireTime, byte nextState) 
            : base(SCOffsets.SCFactionSetRelationStatePacket, 5)
        {
            _id = id;
            _id2 = id2;
            _state = state;
            _expireTime = expireTime;
            _nextState = nextState;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_state);
            stream.Write(_expireTime);
            stream.Write(_nextState);
            return stream;
        }
    }
}

using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionIndependencePacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _id2;
        private readonly string _name;
        private readonly DateTime _cTime; // TODO createTime?
        
        public SCFactionIndependencePacket(uint id, uint id2, string name, DateTime cTime) : base(SCOffsets.SCFactionIndependencePacket, 5)
        {
            _id = id;
            _id2 = id2;
            _name = name;
            _cTime = cTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_id2);
            stream.Write(_name);
            stream.Write(_cTime);
            return stream;
        }
    }
}

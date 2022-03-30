using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitAiAggroPacket : GamePacket
    {
        private readonly uint _npcId;
        private readonly int _count;
        private readonly uint _hostileUnitId;
        private readonly List<int>  _summarizeDamage;
        private readonly byte _topFlags;

        public SCUnitAiAggroPacket(uint npcId, int count, uint hostileUnitId = 0, List<int> summarizeDamage = null,
            byte topFlags = 135) : base(SCOffsets.SCUnitAiAggroPacket, 5)
        {
            _npcId = npcId;
            _count = count;
            _hostileUnitId = hostileUnitId;
            _summarizeDamage = summarizeDamage;
            _topFlags = topFlags;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_npcId);
            stream.Write(_count);

            if (_count <= 0)
                return stream;

            for (var i = 0; i < _count; i++)
            {
                stream.WriteBc(_hostileUnitId);
                foreach (var value in _summarizeDamage)
                    stream.Write(value); // value 

                stream.Write(_topFlags); // topFlags
            }

            return stream;
        }
    }
}

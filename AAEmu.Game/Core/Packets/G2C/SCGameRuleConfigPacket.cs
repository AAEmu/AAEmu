using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGameRuleConfigPacket : GamePacket
    {
        private readonly uint _indunCount;
        private readonly uint _conflictCount;
        private readonly uint _type;
        private readonly byte _pvp;
        private readonly byte _duel;
        private readonly uint _type2;
        private readonly short _peaceMin;


        public SCGameRuleConfigPacket(uint indunCount, uint conflictCount) : base(SCOffsets.SCGameRuleConfigPacket, 5)
        {
            _indunCount = indunCount;
            _conflictCount = conflictCount;
            _type = 0;
            _pvp = 0;
            _duel = 0;
            _type2 = 0;
            _peaceMin = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_indunCount);
            for (var i = 0; i < _indunCount; i++)
            {
                stream.Write(_type); // type
                stream.Write(_pvp); // pvp
                stream.Write(_duel); // duel
            }

            stream.Write(_conflictCount);
            for (var i = 0; i < _conflictCount; i++)
            {
                stream.Write(_type2); // type
                stream.Write(_peaceMin); // peaceMin
            }


            return stream;
        }
    }
}

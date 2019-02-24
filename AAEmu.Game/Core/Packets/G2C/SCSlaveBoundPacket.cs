using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSlaveBoundPacket : GamePacket
    {
        private readonly uint _unkId; // TODO mb slave design?
        private readonly uint _slaveId;

        public SCSlaveBoundPacket(uint unkId, uint slaveId) : base(0x05f, 1)
        {
            _unkId = unkId;
            _slaveId = slaveId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_unkId); // TODO may or may not be. =_+ WTF?!?!?, После 3 раза, я уже ненавижу корейцев.
            stream.Write(_slaveId);
            return stream;
        }

        // TODO if i miss logic
        /*
         * sub_3957BF40
          if ( !a2->Reader->field_14("master", 1, v4) )
            return ReadBc_2(a2, (v2 + 3), "slave", v2 + 3, 0);
          a2->Reader->ReadUInt32("type", v2 + 2, 0);
          a2->Reader->field_18(a2);
          return ReadBc_2(a2, (v2 + 3), "slave", v2 + 3, 0);
         */
    }
}

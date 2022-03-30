using System.Collections;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGameGuardPacket : GamePacket
    {
        private readonly byte _a;
        private readonly uint _b;

        public SCGameGuardPacket(byte a, uint b) : base(SCOffsets.SCGameGuardPacket, 5)
        {
            _a = a;
            _b = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //stream.Write(_a);
            stream.Write(_b);

            return stream;
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUpdatePremiumPointPacket : GamePacket
    {
        private readonly int _point;
        private readonly byte _oldPg;
        private readonly byte _pg;

        public SCUpdatePremiumPointPacket(int point, byte oldPg, byte pg) : base(SCOffsets.SCUpdatePremiumPointPacket, 5)
        {
            _point = point;
            _oldPg = oldPg;
            _pg = pg;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_point);
            stream.Write(_oldPg);
            stream.Write(_pg);
            return stream;
        }
    }
}

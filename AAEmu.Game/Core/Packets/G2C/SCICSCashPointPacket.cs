using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSCashPointPacket : GamePacket
    {
        private readonly int _point;
        private readonly bool _reload;
        
        public SCICSCashPointPacket(int point, bool reload) : base(SCOffsets.SCICSCashPointPacket, 5)
        {
            _point = point;
            _reload = reload;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_point);
            stream.Write(_reload);

            return stream;
        }
    }
}

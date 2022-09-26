using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSCashPointPacket : GamePacket
    {
        private readonly int _point;
        private readonly int _bpoint;
        private readonly bool _reload;
        
        public SCICSCashPointPacket(int point, int bpoint = 0, bool reload = false) : base(SCOffsets.SCICSCashPointPacket, 5)
        {
            _point = point;
            _bpoint = bpoint;
            _reload = reload;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_point);
            stream.Write(_bpoint);
            stream.Write(_reload);
            return stream;
        }
    }
}

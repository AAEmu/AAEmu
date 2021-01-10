using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCICSCashPointPacket : GamePacket
    {
        private readonly int _point;
        
        public SCICSCashPointPacket(int point) : base(SCOffsets.SCICSCashPointPacket, 5)
        {
            _point = point;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_point);
            return stream;
        }
    }
}

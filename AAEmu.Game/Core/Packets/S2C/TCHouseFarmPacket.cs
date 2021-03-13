using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCHouseFarmPacket : StreamPacket
    {
        private readonly string _name;
        private readonly int _total;
        private readonly int _harvestable;
        public TCHouseFarmPacket(string name, int total, int harvestable) : base(0x0E)
        {
            _name = name;
            _total = total;
            _harvestable = harvestable;
        }
        
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_name);        // name
            stream.Write(_total);       // total
            stream.Write(_harvestable); // harvestable

            return stream;
        }
    }
}

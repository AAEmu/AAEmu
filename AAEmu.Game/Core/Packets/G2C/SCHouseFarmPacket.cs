using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseFarmPacket : GamePacket
    {
        private readonly string _name;
        private readonly int _total;
        private readonly int _harvestable;

        public SCHouseFarmPacket(string name, int total, int harvestable) : base(SCOffsets.SCHouseFarmPacket, 5)
        {
            _name = name;
            _total = total;
            _harvestable = harvestable;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_name);
            stream.Write(_total);
            stream.Write(_harvestable);
            return stream;
        }
    }
}

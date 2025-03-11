using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAddHousePacket : GamePacket
    {
        private readonly byte _count;
        private readonly House _house;
        
        public SCAddHousePacket(byte count, House house) : base(SCOffsets.SCAddHousePacket, 5)
        {
            _count = count;
            _house = house;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_count);
            for (var i = 0; i < _count; i++) // TODO не более 20
            {
                _house.Write(stream);
            }

            return stream;
        }
    }
}

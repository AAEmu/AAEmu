using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHousePacket : GamePacket
    {
        private readonly House _house;

        public SCHousePacket(House house) : base(SCOffsets.SCHousePacket, 5)
        {
            _house = house;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _house.Write(stream);
        }
    }
}

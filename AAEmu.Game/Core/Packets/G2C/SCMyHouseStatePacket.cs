using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMyHouseStatePacket : GamePacket
    {
        private readonly House _house;

        public SCMyHouseStatePacket(House house) : base(SCOffsets.SCMyHouseStatePacket, 5)
        {
            _house = house;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _house.Write(stream);
        }
    }
}

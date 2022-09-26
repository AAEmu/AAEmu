using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseStatePacket : GamePacket
    {
        private readonly House _house;
        
        public SCHouseStatePacket(House house) : base(SCOffsets.SCHouseStatePacket, 5)
        {
            _house = house;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _house.Write(stream);
        }
    }
}

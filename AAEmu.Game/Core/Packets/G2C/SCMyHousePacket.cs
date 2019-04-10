using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMyHousePacket : GamePacket
    {
        private readonly House _house;

        public SCMyHousePacket(House house) : base(SCOffsets.SCMyHousePacket, 1)
        {
            _house = house;
        }

        public override PacketStream Write(PacketStream stream)
        {
            return _house.Write(stream);
        }
    }
}

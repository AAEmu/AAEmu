using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLoginCharInfoHousePacket : GamePacket
    {
        private readonly uint _id;
        private readonly House _house;

        public SCLoginCharInfoHousePacket(uint id, House house) : base(SCOffsets.SCLoginCharInfoHousePacket, 5)
        {
            _id = id;
            _house = house;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            return _house.Write(stream);
        }
    }
}

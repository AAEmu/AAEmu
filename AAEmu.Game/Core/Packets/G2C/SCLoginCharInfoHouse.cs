using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLoginCharInfoHouse : GamePacket
    {
        private readonly uint _id;
        private readonly HouseData _houseData;

        public SCLoginCharInfoHouse(uint id, HouseData houseData) : base(0x056, 1)
        {
            _id = id;
            _houseData = houseData;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_houseData);
            return stream;
        }
    }
}

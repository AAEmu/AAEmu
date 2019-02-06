using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLoginCharInfoHouse : GamePacket
    {
        private readonly uint _id;
        private readonly LoginHouseData _loginHouseData;

        public SCLoginCharInfoHouse(uint id, LoginHouseData loginHouseData) : base(0x052, 1)
        {
            _id = id;
            _loginHouseData = loginHouseData;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_loginHouseData);
            return stream;
        }
    }
}

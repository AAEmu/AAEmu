using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFriendStatusChangedPacket : GamePacket
    {
        private readonly Friend _friend;

        public SCFriendStatusChangedPacket(Friend friend) : base(0x051, 1)
        {
            _friend = friend;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_friend);
            return stream;
        }
    }
}

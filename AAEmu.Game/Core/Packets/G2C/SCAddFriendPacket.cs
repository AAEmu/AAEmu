using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAddFriendPacket : GamePacket
    {
        private readonly Friend _friend;
        private readonly bool _success;
        private readonly short _errorMessage;

        public SCAddFriendPacket(Friend friend, bool success, short errorMessage) : base(SCOffsets.SCAddFriendPacket, 5)
        {
            _friend = friend;
            _success = success;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_friend);
            stream.Write(_success);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}

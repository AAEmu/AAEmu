using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSearchListPacket : GamePacket
    {
        private readonly int _total;
        private readonly Friend[] _friends;
        private readonly bool _success;
        
        public SCSearchListPacket(int total, Friend[] friends, bool success) : base(SCOffsets.SCSearchListPacket, 5)
        {
            _total = total;
            _friends = friends;
            _success = success;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_total);
            stream.Write(_friends.Length); // TODO max length 200
            foreach (var friend in _friends)
                stream.Write(friend);
            stream.Write(_success);
            return stream;
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddFriendPacket : GamePacket
    {
        public CSAddFriendPacket() : base(CSOffsets.CSAddFriendPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            
            _log.Debug("AddFriend, name: {0}", name);
            Connection.ActiveChar.Friends.AddFriend(name);
        }
    }
}

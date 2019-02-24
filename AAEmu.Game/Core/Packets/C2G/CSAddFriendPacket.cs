using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddFriendPacket : GamePacket
    {
        public CSAddFriendPacket() : base(0x101, 1)
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

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddFriendPacket : GamePacket
    {
        public CSAddFriendPacket() : base(0x104, 1) // 0x101
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            
            _log.Debug("AddFriend, name: {0}", name);
            DbLoggerCategory.Database.Connection.ActiveChar.Friends.AddFriend(name);
        }
    }
}

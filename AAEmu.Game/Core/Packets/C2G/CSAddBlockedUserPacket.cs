using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddBlockedUserPacket : GamePacket
    {
        public CSAddBlockedUserPacket() : base(0x107, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            _log.Warn("AddBlockedUser, {0}", name);
            DbLoggerCategory.Database.Connection.ActiveChar.Blocked.AddBlockedUser(name);
        }
    }
}

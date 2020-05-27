using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTradeLockPacket : GamePacket
    {
        public CSTradeLockPacket() : base(0x0f3, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var _lock = stream.ReadBoolean();
            TradeManager.Instance.LockTrade(DbLoggerCategory.Database.Connection.ActiveChar, _lock);
        }
    }
}

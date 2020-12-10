using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTradeLockPacket : GamePacket
    {
        public CSTradeLockPacket() : base(CSOffsets.CSTradeLockPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var _lock = stream.ReadBoolean();
            TradeManager.Instance.LockTrade(Connection.ActiveChar, _lock);
        }
    }
}

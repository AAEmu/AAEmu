using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTradeLockPacket : GamePacket
    {
        public CSTradeLockPacket() : base(0x0f0, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var _lock = stream.ReadBoolean();
            
            _log.Warn("TradeLock, Lock: {0}", _lock);
        }
    }
}

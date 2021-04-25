using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FlushMsgsPacket : GamePacket
    {
        // TODO Only command without body...
        public FlushMsgsPacket() : base(PPOffsets.FlushMsgsPacket, 2)
        {
        }
    }
}

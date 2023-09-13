using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class PartialAspectPacket : GamePacket
    {
        // TODO Only command without body...
        public PartialAspectPacket() : base(PPOffsets.PartialAspectPacket, 2)
        {
        }
    }
}

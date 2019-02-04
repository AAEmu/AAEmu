using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class UpdateAspectPacket : GamePacket
    {
        // TODO Only command without body...
        public UpdateAspectPacket() : base(0x00c, 2)
        {
        }
    }
}
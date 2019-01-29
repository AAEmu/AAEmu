using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class BeginBindObjPacket : GamePacket
    {
        // TODO Only command without body...
        public BeginBindObjPacket() : base(0x007, 2)
        {
        }
    }
}
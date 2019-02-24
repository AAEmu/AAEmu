using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class RemoveStaticObjPacket : GamePacket
    {
        // TODO Only command without body...
        public RemoveStaticObjPacket() : base(0x00a, 2)
        {
        }
    }
}
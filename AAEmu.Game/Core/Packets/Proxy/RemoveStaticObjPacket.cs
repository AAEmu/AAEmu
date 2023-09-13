using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class RemoveStaticObjPacket : GamePacket
    {
        // TODO Only command without body...
        public RemoveStaticObjPacket() : base(PPOffsets.RemoveStaticObjPacket, 2)
        {
        }
    }
}

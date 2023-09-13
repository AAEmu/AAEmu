using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class EndBindObjPacket : GamePacket
    {
        // TODO Only command without body...
        public EndBindObjPacket() : base(PPOffsets.EndBindObjPacket, 2)
        {
        }
    }
}

using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class EndUpdateObjPacket : GamePacket
    {
        // TODO Only command without body...
        public EndUpdateObjPacket() : base(PPOffsets.EndUpdateObjPacket, 2)
        {
        }
    }
}

using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class BeginUpdateObjPacket : GamePacket
    {
        // TODO Only command without body...
        public BeginUpdateObjPacket() : base(PPOffsets.BeginUpdateObjPacket, 2)
        {
        }
    }
}

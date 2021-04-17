using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class UnbindPredictedObjPacket : GamePacket
    {
        // TODO Only command without body...
        public UnbindPredictedObjPacket() : base(PPOffsets.UnbindPredictedObjPacket, 2)
        {
        }
    }
}

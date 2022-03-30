using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBeautyshopBypassPacket : GamePacket
    {
        public CSBeautyshopBypassPacket() : base(CSOffsets.CSBeautyshopBypassPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSBeautyshopBypassPacket");
        }
    }
}

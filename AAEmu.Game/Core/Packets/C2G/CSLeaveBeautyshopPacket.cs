using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveBeautyshopPacket : GamePacket
    {
        public CSLeaveBeautyshopPacket() : base(CSOffsets.CSLeaveBeautyshopPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSLeaveBeautyshopPacket");
        }
    }
}

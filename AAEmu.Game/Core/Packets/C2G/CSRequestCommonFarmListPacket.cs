using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestCommonFarmListPacket : GamePacket
    {
        public CSRequestCommonFarmListPacket() : base(CSOffsets.CSRequestCommonFarmListPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestCommonFarmListPacket");
        }
    }
}

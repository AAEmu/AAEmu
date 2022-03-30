using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCheckSecondPassPacket : GamePacket
    {
        public CSCheckSecondPassPacket() : base(CSOffsets.CSCheckSecondPassPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSCheckSecondPassPacket");
        }
    }
}

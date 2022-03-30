using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSClearSecondPassPacket : GamePacket
    {
        public CSClearSecondPassPacket() : base(CSOffsets.CSClearSecondPassPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSClearSecondPassPacket");
        }
    }
}

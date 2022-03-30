using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeSecondPassPacket : GamePacket
    {
        public CSChangeSecondPassPacket() : base(CSOffsets.CSChangeSecondPassPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSChangeSecondPassPacket");
        }
    }
}

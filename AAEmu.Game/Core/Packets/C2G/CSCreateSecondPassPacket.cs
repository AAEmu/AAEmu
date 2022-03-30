using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateSecondPassPacket : GamePacket
    {
        public CSCreateSecondPassPacket() : base(CSOffsets.CSCreateSecondPassPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSCreateSecondPassPacket");
        }
    }
}

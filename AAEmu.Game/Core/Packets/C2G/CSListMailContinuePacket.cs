using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListMailContinuePacket : GamePacket
    {
        public CSListMailContinuePacket() : base(CSOffsets.CSListMailContinuePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("ListMailContinue");
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSProtectSensitiveOperationPacket : GamePacket
    {
        public CSProtectSensitiveOperationPacket() : base(CSOffsets.CSProtectSensitiveOperationPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSProtectSensitiveOperationPacket");
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetupSecondPasswordPacket : GamePacket
    {
        public CSSetupSecondPasswordPacket() : base(CSOffsets.CSSetupSecondPasswordPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("SetupSecondPassword");
        }
    }
}

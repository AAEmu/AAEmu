using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetupSecondPassword : GamePacket
    {
        public CSSetupSecondPassword() : base(CSOffsets.CSSetupSecondPassword, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("SetupSecondPassword");
        }
    }
}

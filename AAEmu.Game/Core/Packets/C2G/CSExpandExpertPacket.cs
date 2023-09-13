using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpandExpertPacket : GamePacket
    {
        public CSExpandExpertPacket() : base(CSOffsets.CSExpandExpertPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("ExpandExpert");

            Connection.ActiveChar.Actability.ExpandExpert();
        }
    }
}

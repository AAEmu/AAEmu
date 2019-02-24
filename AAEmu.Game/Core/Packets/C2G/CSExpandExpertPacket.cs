using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpandExpertPacket : GamePacket
    {
        public CSExpandExpertPacket() : base(0x0fe, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("ExpandExpert");

            Connection.ActiveChar.Actability.ExpandExpert();
        }
    }
}

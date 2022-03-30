using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestPermissionToPlayCinemaForDirectingModePacket : GamePacket
    {
        public CSRequestPermissionToPlayCinemaForDirectingModePacket() : base(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingModePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestPermissionToPlayCinemaForDirectingModePacket");
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEndPortalInteractionPacket : GamePacket
    {
        public CSEndPortalInteractionPacket() : base(CSOffsets.CSEndPortalInteractionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSEndPortalInteractionPacket");
        }
    }
}

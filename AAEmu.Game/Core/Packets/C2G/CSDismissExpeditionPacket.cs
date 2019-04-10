using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDismissExpeditionPacket : GamePacket
    {
        public CSDismissExpeditionPacket() : base(0x00b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("DismissExpedition");
        }
    }
}

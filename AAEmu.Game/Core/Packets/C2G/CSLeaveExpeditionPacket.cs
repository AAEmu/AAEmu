using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveExpeditionPacket : GamePacket
    {
        public CSLeaveExpeditionPacket() : base(0x00e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("LeaveExpedition");
        }
    }
}

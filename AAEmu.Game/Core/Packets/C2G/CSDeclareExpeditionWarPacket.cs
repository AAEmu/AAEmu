using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDeclareExpeditionWarPacket : GamePacket
    {
        public CSDeclareExpeditionWarPacket() : base(CSOffsets.CSDeclareExpeditionWarPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSDeclareExpeditionWarPacket");
        }
    }
}

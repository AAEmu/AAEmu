using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionShowRenameUIPacket : GamePacket
    {
        public SCExpeditionShowRenameUIPacket() : base(SCOffsets.SCExpeditionShowRenameUIPacket, 5)
        {
        }
    }
}

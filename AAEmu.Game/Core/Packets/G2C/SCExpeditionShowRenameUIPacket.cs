using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionShowRenameUIPacket : GamePacket
    {
        public SCExpeditionShowRenameUIPacket() : base(0x00e, 1)
        {
        }
    }
}

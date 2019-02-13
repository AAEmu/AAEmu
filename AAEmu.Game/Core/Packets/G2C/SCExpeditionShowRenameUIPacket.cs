using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionShowRenameUIPacket : GamePacket
    {
        public SCExpeditionShowRenameUIPacket() : base(0x00e, 1) // TODO 1.0 opcode: 0x00d
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            return stream;
        }
    }
}

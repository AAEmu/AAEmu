using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeaveWorldCanceledPacket : GamePacket
    {
        public SCLeaveWorldCanceledPacket() : base(0x004, 1)
        {
        }
    }
}

using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeaveWorldCanceledPacket : GamePacket
    {
        public SCLeaveWorldCanceledPacket() : base(SCOffsets.SCLeaveWorldCanceledPacket, 1)
        {
        }
    }
}

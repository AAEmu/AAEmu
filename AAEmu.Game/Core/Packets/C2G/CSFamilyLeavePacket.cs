using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyLeavePacket : GamePacket
    {
        public CSFamilyLeavePacket() : base(0x01b, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("FamilyLeave");
        }
    }
}

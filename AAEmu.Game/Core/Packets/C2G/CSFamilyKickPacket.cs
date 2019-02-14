using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyKickPacket : GamePacket
    {
        public CSFamilyKickPacket() : base(0x01d, 1)  //TODO : 1.0 opcode: 0x01c
        {
        }

        public override void Read(PacketStream stream)
        {
            var memberId = stream.ReadUInt32();

            _log.Debug("FamilyKick, memberId: {0}", memberId);
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyChangeTitlePacket : GamePacket
    {
        public CSFamilyChangeTitlePacket() : base(0x01e, 1)  //TODO : 1.0 opcode: 0x01d
        {
        }

        public override void Read(PacketStream stream)
        {
            var memberId = stream.ReadUInt32();
            var title = stream.ReadString();

            _log.Debug("FamilyChangeTitle, memberId: {0}, title: {1}", memberId, title);
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListMailPacket : GamePacket
    {
        public CSListMailPacket() : base(CSOffsets.CSListMailPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            byte mailType = stream.ReadByte();
            uint startIndex = stream.ReadUInt32();
            uint sendCount = stream.ReadUInt32();
            bool isRecover = stream.ReadBoolean();
            bool isTest = stream.ReadBoolean();
            // Empty struct

            _log.Error("CSListMailPacket     MailType: {0}, SIndex: {1}, SCount: {2}, isRecover {3}, isTest {4} ",
                mailType, startIndex, sendCount, isRecover, isTest);
            Connection.ActiveChar.Mails.OpenMailbox();
        }
    }
}

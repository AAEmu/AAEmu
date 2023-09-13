using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReadMailPacket : GamePacket
    {
        public CSReadMailPacket() : base(CSOffsets.CSReadMailPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var isSent = stream.ReadBoolean();
            var mailId = stream.ReadInt64();

            _log.Debug("ReadMail, Id: {0}, isSent: {1}", mailId, isSent);
            Connection.ActiveChar.Mails.ReadMail(isSent, mailId);
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakeAttachmentSequentially : GamePacket
    {
        public CSTakeAttachmentSequentially() : base(0x09f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var mailId = stream.ReadInt64();
            _log.Debug("TakeAttachmentSequentially, mailId: {0}", mailId);
            var mail = MailManager.Instance.GetMailById(mailId);
            if (mail.Header.ReceiverId != Connection.ActiveChar.Id) // just a check for hackers trying to steal mails
            {
                Connection.ActiveChar.SendErrorMessage(Models.Game.Error.ErrorMessageType.MailInvalid);
            }
            else
            {
                Connection.ActiveChar.Mails.GetAttached(mailId, true, true, true);
            }
        }
    }
}

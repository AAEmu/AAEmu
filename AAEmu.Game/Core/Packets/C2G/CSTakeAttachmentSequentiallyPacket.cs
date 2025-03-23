using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Mails.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTakeAttachmentSequentiallyPacket : GamePacket
{
    public CSTakeAttachmentSequentiallyPacket() : base(CSOffsets.CSTakeAttachmentSequentiallyPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var takeMoney = false;
        var takeItems = false;
        var takeAllSelected = true;

        var mailId = stream.ReadInt64();

        Logger.Debug("TakeAttachmentSequentially, mailId: {0}", mailId);
        var mail = MailManager.Instance.GetMailById(mailId);
        if (mail is null)
        {
            return;
        }
        if (mail.Header.ReceiverId != Connection.ActiveChar.Id) // just a check for hackers trying to steal mails
        {
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailInvalid);
        }
        else
        {
            if (mail.Body.CopperCoins > 0 || mail.Body.BillingAmount > 0 || mail.Body.MoneyAmount2 > 0)
            {
                takeMoney = true;
            }

            if (mail.Body.Attachments.Count > 0)
            {
                takeItems = true;
            }

            if (mail.MailType == MailType.Charged)
            {
                takeAllSelected = false;
            }

            if (Connection.ActiveChar.Mails.GetAttached(mailId, takeMoney, takeItems, takeAllSelected))
            {
                Connection.ActiveChar.SendPacket(new SCMailStatusUpdatedPacket(false, mailId, MailStatus.Read));
                Connection.ActiveChar.Mails.SendUnreadMailCount();
                return;
            }

            Logger.Debug($"CSTakeAllAttachmentItemPacket - Failed for: {mailId} -> {Connection.ActiveChar.Name}");
        }
    }
}

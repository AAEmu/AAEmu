using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Mails.Static;

namespace AAEmu.Game.Models.Game.Mails;

public class MailHeader : PacketMarshaler
{
    private BaseMail _baseMail;
    public long MailId { get => _baseMail.Id; }
    public MailType Type { get => _baseMail.MailType; }
    public MailStatus Status { get; set; }
    public string Title { get => _baseMail.Title; } // TODO max length 400
    public uint SenderId { get; set; }
    public string SenderName { get; set; } // TODO max length 128
    public byte Attachments { get; set; }
    public uint ReceiverId { get; set; }
    public string ReceiverName { get => _baseMail.ReceiverName; } // TODO max length 128
    public DateTime OpenDate { get => _baseMail.OpenDate; }
    public bool Returned { get; set; }
    public long Extra { get; set; }

    public MailHeader(BaseMail parent)
    {
        _baseMail = parent;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(MailId);
        stream.Write((byte)Type);
        stream.Write((byte)Status);
        stream.Write(Title);
        stream.Write(SenderName);
        stream.Write(Attachments);
        stream.Write(ReceiverName);
        stream.Write(OpenDate);
        stream.Write(Returned);
        stream.Write(Extra);
        return stream;
    }
}

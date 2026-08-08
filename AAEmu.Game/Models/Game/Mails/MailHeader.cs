using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails;

public class MailHeader(BaseMail parent) : PacketMarshaler
{
    public long MailId { get => parent.Id; }
    public MailType Type { get => parent.MailType; }
    public MailStatus Status { get; set; }
    public string Title { get => parent.Title; } // TODO max length 400
    public uint SenderId { get; set; }
    public string SenderName { get; set; } // TODO max length 128
    public byte Attachments { get; set; }
    public uint ReceiverId { get; set; }
    public string ReceiverName { get => parent.ReceiverName; } // TODO max length 128
    public DateTime OpenDate { get => parent.OpenDate; }
    public bool Returned { get; set; }
    public long Extra { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        // Keep the header complete so an optional body remains aligned.
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
        stream.Write(false);
        return stream;
    }
}

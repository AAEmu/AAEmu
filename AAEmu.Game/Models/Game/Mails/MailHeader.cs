using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails
{
    public class MailHeader : PacketMarshaler
    {
        public long mailId { get; set; }
        public MailType Type { get; set; }
        public MailStatus Status { get; set; }
        public string Title { get; set; } // TODO max length 400
        public uint SenderId { get; set; }
        public string SenderName { get; set; } // TODO max length 128
        public byte Attachments { get; set; }
        public uint ReceiverId { get; set; }
        public string ReceiverName { get; set; } // TODO max length 128
        public DateTime OpenDate { get; set; }
        public bool Returned { get; set; }
        public long Extra { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(mailId);
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
}

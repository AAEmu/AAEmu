using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails
{
    public class CountUnreadMail : PacketMarshaler
    {
        public int Sent { get; set; }
        public int Received { get; protected set; }
        public int MiaReceived { get; protected set; }
        public int CommercialReceived { get; protected set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Sent);
            stream.Write(Received);
            stream.Write(MiaReceived);
            stream.Write(CommercialReceived);
            return stream;
        }

        public void ResetReceived()
        {
            Received = 0;
            MiaReceived = 0;
            CommercialReceived = 0;
        }

        public void UpdateReceived(MailType mailType, int amount)
        {
            if ((mailType == MailType.Charged) || (mailType == MailType.Promotion))
            {
                CommercialReceived += amount;
            }
            else
            if (mailType == MailType.MiaRecv)
            {
                MiaReceived += amount;
            }
            else
            {
                Received += amount;
            }
        }
        
    }
}

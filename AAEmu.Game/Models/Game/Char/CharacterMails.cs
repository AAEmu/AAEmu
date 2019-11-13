using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterMails
    {
        public Character Owner { get; set; }

        public CharacterMails(Character owner)
        {
            Owner = owner;
        }

        public void Send()
        {
            // Owner.SendPacket(new SCMailListPacket(false, new Mail[0]));
            Owner.SendPacket(new SCMailListEndPacket(0, 0));
        }

        public void Load(GameDBContext ctx)
        {
        }

        public void Save(GameDBContext ctx)
        {
        }
    }
}

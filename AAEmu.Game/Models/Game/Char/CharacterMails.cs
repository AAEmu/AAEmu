using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails;
using MySql.Data.MySqlClient;

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

        public void Load(MySqlConnection connection)
        {
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
        }
    }
}

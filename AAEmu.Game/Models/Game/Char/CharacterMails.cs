using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using MySql.Data.MySqlClient;

/****TODO****
* 1. When player reads the mail send a packet to make it show as read in the client mail list.
* 2. After grabbing money from the mail attachments a packet needs to be sent to "reset" and allow you to get money from another mail.
* 3. Items in mail. (Line 102 doesn't use the DB)
* 4. Update entire system to work with persistent and dynamic data instead of the Save()/Load() system right now. (Refer to Nike's housing)
* 5. Solve the double click issue when trying to read mail. (It's because we don't send the body to the player until after they try to read it)
* 6. Reset money value and include all sources of money in the reset. 
* 7. Send mail.
* 
* CORE SQL:
DROP TABLE IF EXISTS `mails`;
 SET character_set_client = utf8mb4;
CREATE TABLE `mails` (
  `id` int (11) unsigned NOT NULL,
  `type` tinyint(4) NOT NULL,
  `status` tinyint(4) NOT NULL,
  `title` varchar(400) NOT NULL,
  `sender_name` varchar(128) NOT NULL,
  `attachments` tinyint(4) NOT NULL,
  `receiver_name` varchar(128) NOT NULL,
  `open_date` datetime NOT NULL,
  `returned` tinyint(4) NOT NULL,
  `extra` int (11) NOT NULL,
   PRIMARY KEY(`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

DROP TABLE IF EXISTS `mails_body`;
 SET character_set_client = utf8mb4;
CREATE TABLE `mails_body` (
  `id` int (11) unsigned NOT NULL,
  `type` int (1) NOT NULL,
  `receiver_name` varchar(128) NOT NULL,
  `title` varchar(400) NOT NULL,
  `text` varchar(1200) NOT NULL,
  `money_amount_1` bigint(20) NOT NULL,
  `money_amount_2` bigint(20) NOT NULL,
  `money_amount_3` bigint(20) NOT NULL,
  `send_date` datetime NOT NULL,
  `received_date` datetime NOT NULL,
  `open_date` datetime NOT NULL,
  `items` varchar(1200) NOT NULL,
  PRIMARY KEY(`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE = utf8mb4_0900_ai_ci;
*************/

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterMails
    {
        public CountUnreadMail unreadMailCount;
        public Character Self { get; set; }
        public List<Mail> playerMails;
        public List<MailBody> playerMailsBody;

        public CharacterMails(Character self)
        {
            unreadMailCount = new CountUnreadMail
            {
                Sent = 0,
                Received = 0,
                MiaReceived = 0,
                CommercialReceived = 0
            };
            Self = self;
            playerMails = new List<Mail>();
            playerMailsBody = new List<MailBody>();

        }

        public void Send()
        {
            foreach (var m in playerMails)
            {
                var tempArray = new Mail[] { m };
                Self.SendPacket(new SCMailListPacket(false, tempArray));
            }
            Self.SendPacket(new SCMailListEndPacket(0, 0));
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM mails WHERE `receiver_name` = @self";
                command.Parameters.AddWithValue("@self", Self.Name);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var mailTemplate = new Mail()
                        {
                            Id = reader.GetUInt32("id"),
                            Type = (byte)reader.GetInt32("type"),
                            Status = (byte)reader.GetInt32("status"),
                            Title = reader.GetString("title"),
                            SenderName = reader.GetString("sender_name"),
                            Attachments = (byte)reader.GetInt32("attachments"),
                            ReceiverName = reader.GetString("receiver_name"),
                            OpenDate = reader.GetDateTime("open_date"),
                            Returned = (byte)reader.GetInt32("returned"),
                            Extra = reader.GetUInt32("extra")
                        };
                        playerMails.Add(mailTemplate);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM mails_body WHERE `receiver_name` = @self";
                command.Parameters.AddWithValue("@self", Self.Name);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var mailBodyTemplate = new MailBody()
                        {
                            Id = reader.GetUInt32("id"),
                            Type = (byte)reader.GetInt32("type"),
                            ReceiverName = reader.GetString("receiver_name"),
                            Title = reader.GetString("title"),
                            Text = reader.GetString("text"),
                            MoneyAmount1 = reader.GetInt32("money_amount_1"),
                            MoneyAmount2 = reader.GetInt32("money_amount_2"),
                            MoneyAmount3 = reader.GetInt32("money_amount_3"),
                            SendDate = reader.GetDateTime("send_date"),
                            RecvDate = reader.GetDateTime("received_date"),
                            OpenDate = reader.GetDateTime("open_date"),
                            Items = new Item[10] //TODO: Pull items from DB instead
                        };
                        playerMailsBody.Add(mailBodyTemplate);
                    }
                }
            }

            foreach (var m in playerMails)
            {
                if (m.OpenDate == System.DateTime.MinValue)
                {
                    unreadMailCount.Received += 1;
                }
                Self.SendPacket(new SCGotMailPacket(m, unreadMailCount, false, playerMailsBody.Find(x => x.Id.Equals(m.Id))));
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            foreach (var m in playerMailsBody)
            {
                if (m.OpenDate != System.DateTime.MinValue)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;

                        command.CommandText = "UPDATE mails SET `open_date` = @openDate WHERE `id` = @id";
                        command.Prepare();
                        command.Parameters.AddWithValue("@openDate", m.OpenDate);
                        command.Parameters.AddWithValue("@id", m.Id);
                        command.ExecuteNonQuery();
                    }
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;

                        command.CommandText = "UPDATE mails_body SET `open_date` = @openDate, `money_amount_1` = @money WHERE `id` = @id";
                        command.Prepare();
                        command.Parameters.AddWithValue("@openDate", m.OpenDate);
                        command.Parameters.AddWithValue("@id", m.Id);
                        command.Parameters.AddWithValue("@money", m.MoneyAmount1);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public void ReadMail(bool isSent, long id)
        {
            var mb = playerMailsBody.Find(x => x.Id.Equals(id));
            if (mb != null)
            {
                if (mb.OpenDate == System.DateTime.MinValue)
                {
                    unreadMailCount.Received -= 1;
                    mb.OpenDate = System.DateTime.UtcNow;
                    Self.SendPacket(new SCMailBodyPacket(true, isSent, mb, false, unreadMailCount));
                }
                else
                {
                    Self.SendPacket(new SCMailBodyPacket(true, isSent, mb, true, unreadMailCount));
                }
            }
        }

        public void CountMail()
        {
            Self.SendPacket(new SCCountUnreadMailPacket(unreadMailCount));
        }

        public void GetAttachedMoney(long id)
        {
            var mb = playerMailsBody.Find(x => x.Id.Equals(id));
            if (mb != null)
            {
                Self.Money += mb.MoneyAmount1;
                Self.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Mail, new List<ItemTask> { new MoneyChange(mb.MoneyAmount1) }, new List<ulong>()));
                //Todo: Set money to 0 after successfully obtaining it
                
            }
        }
    }
}

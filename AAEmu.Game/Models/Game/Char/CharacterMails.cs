using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using MySql.Data.MySqlClient;
using NLog;

/****TODO****
* 1. When player reads the mail send a packet to make it show as read in the client mail list.
* 2. After grabbing money from the mail attachments a packet needs to be sent to "reset" and allow you to get money from another mail.
* 3. Items in mail.
* 4. Update system to work with persistent and dynamic data instead of the Save()/Load() system right now. (Refer to Nike's housing)
* 5. Send mail.
*/

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterMails
    {
        public CountUnreadMail unreadMailCount;
        public Character Self { get; set; }
        public Dictionary<long, Tuple<Mail,MailBody,bool>> mail; //mail, body, isSent

        private static Logger log = LogManager.GetCurrentClassLogger();

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
            mail = new Dictionary<long, Tuple<Mail,MailBody,bool>>();

        }

        public void Load(MySqlConnection connection) //first load
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM mails WHERE `receiver_name` = @self OR `sender_name` = @self";
                command.Parameters.AddWithValue("@self", Self.Name);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!mail.ContainsKey(reader.GetInt32("id"))) {
                            var mailTemplate = new Mail()
                            {
                                Id = reader.GetInt32("id"),
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
                            var mailBodyTemplate = new MailBody()
                            {
                                Id = mailTemplate.Id,
                                Type = mailTemplate.Type,
                                ReceiverName = mailTemplate.ReceiverName,
                                Title = mailTemplate.Title,
                                Text = reader.GetString("text"),
                                MoneyAmount1 = reader.GetInt32("money_amount_1"),
                                MoneyAmount2 = reader.GetInt32("money_amount_2"),
                                MoneyAmount3 = reader.GetInt32("money_amount_3"),
                                SendDate = reader.GetDateTime("send_date"),
                                RecvDate = reader.GetDateTime("received_date"),
                                OpenDate = mailTemplate.OpenDate,
                                Items = new Item[10] //TODO: Pull items from DB instead
                            };
                            if (mailTemplate.OpenDate == DateTime.MinValue && mailTemplate.SenderName != Self.Name)
                                unreadMailCount.Received += 1;
                            Self.SendPacket(new SCGotMailPacket(mailTemplate, unreadMailCount, false, mailBodyTemplate));
                            if (mailTemplate.SenderName == Self.Name)
                                mail.Add(mailTemplate.Id, new Tuple<Mail, MailBody, bool>(mailTemplate, mailBodyTemplate, true));
                            else
                                mail.Add(mailTemplate.Id, new Tuple<Mail, MailBody, bool>(mailTemplate, mailBodyTemplate, false));
                        }
                    }
                }
            }
            
        }

        public void Send() //open mailbox
        {
            foreach (KeyValuePair<long, Tuple<Mail, MailBody, bool>> m in mail)
                if (!m.Value.Item3)
                    Self.SendPacket(new SCMailListPacket(false, new Mail[] { m.Value.Item1 }));     
                else
                    Self.SendPacket(new SCMailListPacket(true, new Mail[] { m.Value.Item1 }));
            Self.SendPacket(new SCMailListEndPacket(0, 0));
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction) //Log off
        {
            string tempCommand;
            foreach (KeyValuePair<long, Tuple<Mail, MailBody, bool>> m in mail)
            {
                if (m.Value.Item2.OpenDate == DateTime.MinValue && m.Value.Item1.OpenDate > DateTime.MinValue && m.Value.Item3 == false)
                {
                    tempCommand = "UPDATE mails SET `open_date` = @openDate, `money_amount_1` = @money WHERE `id` = @id";
                    log.Debug("0: " + m.Value.Item2.OpenDate + " | " + m.Value.Item3);
                }
                else if (m.Value.Item2.OpenDate > DateTime.MinValue && m.Value.Item3 == false)
                {
                    tempCommand = "UPDATE mails SET `money_amount_1` = @money WHERE `id` = @id";
                    log.Debug("1: " + m.Value.Item2.OpenDate + " | " + m.Value.Item3);
                }
                else
                {
                    tempCommand = "";
                    log.Debug("2: "+ m.Value.Item2.OpenDate + " | " + m.Value.Item3);
                }
                if (tempCommand != "")
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;
                        command.CommandText = tempCommand;
                        command.Prepare();
                        command.Parameters.AddWithValue("@openDate", m.Value.Item1.OpenDate);
                        command.Parameters.AddWithValue("@id", m.Value.Item1.Id);
                        command.Parameters.AddWithValue("@money", m.Value.Item2.MoneyAmount1);
                        command.ExecuteNonQuery();
                    }
            }
        }

        public void ReadMail(bool isSent, long id) //click on mail in box
        {
            if (mail.ContainsKey(id))
                if (mail[id].Item1.OpenDate == DateTime.MinValue && !isSent)
                {
                    unreadMailCount.Received -= 1;
                    mail[id].Item1.OpenDate = DateTime.UtcNow;
                }
        }

        public void CountMail() //When character is spawning
        {
            bool openDateModified;
            Self.SendPacket(new SCCountUnreadMailPacket(unreadMailCount));
            foreach (KeyValuePair<long, Tuple<Mail, MailBody, bool>> m in mail)
            {
                if (m.Value.Item1.OpenDate == DateTime.MinValue)
                    openDateModified = false;
                else
                    openDateModified = true;
                Self.SendPacket(new SCMailBodyPacket(true, m.Value.Item3, m.Value.Item2, openDateModified, unreadMailCount));
            }
        }

        public void GetAttachedMoney(long id) //Clicking left attachment button in mail body
        {
            if (mail.ContainsKey(id))
            {
                Self.Money += mail[id].Item2.MoneyAmount1; //10550 = 1 gold, 5 silver, 50 bronze
                Self.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Mail, new List<ItemTask> { new MoneyChange(mail[id].Item2.MoneyAmount1) }, new List<ulong>()));
                mail[id].Item2.MoneyAmount1 = 0;
            }
        }
    }
}

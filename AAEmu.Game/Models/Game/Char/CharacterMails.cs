using System;
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
* 5. Send mail.
* 6. Open date doesn't seem to get modified after read. REDO the update
*/

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterMails
    {
        public CountUnreadMail unreadMailCount;
        public Character Self { get; set; }
        public Dictionary<long, Tuple<Mail,MailBody,bool>> mail; //mail, body, isSent
        public long latestID; //Temp, will need to be updated for persistent data

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
                                Text = reader.GetString("text"), //TODO MAX LENGTH 1600
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
                command.CommandText = "SELECT MAX(`id`) FROM mails";
                latestID = (int)command.ExecuteScalar() + 1;
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
                if (m.Value.Item1.OpenDate == DateTime.MinValue && !m.Value.Item3)
                    tempCommand = "UPDATE mails SET `open_date` = @openDate, `money_amount_1` = @money WHERE `id` = @id";
                else
                    tempCommand = "UPDATE mails SET `money_amount_1` = @money WHERE `id` = @id";
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

                if (m.Value.Item3) //sent mail
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;

                        command.CommandText = "REPLACE INTO mails(`type`,`status`,`title`,`text`,`sender_name`,`attachments`," +
                                              "`receiver_name`,`open_date`,`send_date`,`received_date`,`returned`,`extra`,`money_amount_1`," +
                                              "`money_amount_2`,`money_amount_3`,`items`) VALUES (@type, @status, @title, @text, @senderName," +
                                              " @attachments, @receiverName, @openDate, @sendDate, @receivedDate, @returned, @extra, @money1, @money2, @money3, @items)";
                        command.Parameters.AddWithValue("@type", m.Value.Item1.Type);
                        command.Parameters.AddWithValue("@status", m.Value.Item1.Status);
                        command.Parameters.AddWithValue("@title", m.Value.Item1.Title);
                        command.Parameters.AddWithValue("@text", m.Value.Item2.Text);
                        command.Parameters.AddWithValue("@senderName", m.Value.Item1.SenderName);
                        command.Parameters.AddWithValue("@attachments", m.Value.Item1.Attachments);
                        command.Parameters.AddWithValue("@receiverName", m.Value.Item1.ReceiverName);
                        command.Parameters.AddWithValue("@openDate", m.Value.Item1.OpenDate);
                        command.Parameters.AddWithValue("@sendDate", DateTime.UtcNow);
                        command.Parameters.AddWithValue("@receivedDate", DateTime.UtcNow);
                        command.Parameters.AddWithValue("@returned", m.Value.Item1.Returned);
                        command.Parameters.AddWithValue("@extra", m.Value.Item1.Extra);
                        command.Parameters.AddWithValue("@money1", m.Value.Item2.MoneyAmount1);
                        command.Parameters.AddWithValue("@money2", m.Value.Item2.MoneyAmount2);
                        command.Parameters.AddWithValue("@money3", m.Value.Item2.MoneyAmount3);
                        command.Parameters.AddWithValue("@items", m.Value.Item2.Items);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public void ReadMail(bool isSent, long id) //click on mail in box
        {
            if (mail.ContainsKey(id))
                if (mail[id].Item1.OpenDate == DateTime.MinValue && !isSent)
                {
                    unreadMailCount.Received -= 1;
                    mail[id].Item1.OpenDate = DateTime.Now;
                }
        }

        public void SendMail(byte type, string receiverName, uint unkId, string title, string text, byte attachments, int[] moneyAmounts, long extra, (SlotType slotType, byte slot)[] itemSlots) //click on send mail in box
        {
            var mailTemplate = new Mail()
            {
                Id = 0,
                Type = type,
                Status = (byte)0,
                Title = title,
                SenderName = Self.Name,
                Attachments = attachments,
                ReceiverName = receiverName,
                OpenDate = DateTime.MinValue,
                Returned = (byte)0,
                Extra = extra
            };
            var mailBodyTemplate = new MailBody()
            {
                Id = mailTemplate.Id,
                Type = mailTemplate.Type,
                ReceiverName = mailTemplate.ReceiverName,
                Title = mailTemplate.Title,
                Text = text, //TODO MAX LENGTH 1600
                MoneyAmount1 = moneyAmounts[0],
                MoneyAmount2 = moneyAmounts[1],
                MoneyAmount3 = moneyAmounts[2],
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow,
                OpenDate = mailTemplate.OpenDate,
                Items = new Item[10] //TODO: Add items to DB instead
            };
            Self.SendPacket(new SCMailSentPacket(mailTemplate, itemSlots));
            mail.Add(latestID, new Tuple<Mail, MailBody, bool>(mailTemplate, mailBodyTemplate, true));
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

        public void GetAttachedMoney(long id) //Clicking left attachment item in mail body
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

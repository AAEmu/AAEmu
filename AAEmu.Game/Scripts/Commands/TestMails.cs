using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestMails : ICommand
    {
        public void OnLoad()
        {
            string[] names = { "testmail", "test_mail" };
            CommandManager.Instance.Register(names, this);
        }

        public string GetCommandLineHelp()
        {
            return "<mailtype> [sendername] [title] [body] [money] [extraflag] [attachmentitems ID/Counts]";
        }

        public string GetCommandHelpText()
        {
            return "Sends a dummy mail to yourself of given type";
        }

        public void Execute(Character character, string[] args)
        {

            // Example: Dummy pack base 1g at 125% no bonus, seller is not the crafter
            // /testmail 19 .sellBackpack Payment "body('Dummy Pack', 125, 12500, 10000, 0, 10500, 0, 1, 0, 0)"

            // Example: Dummy pack base 1g at 125% no bonus, seller is crafter
            // /testmail 19 .sellBackpack Payment "body('Dummy Pack', 125, 12500, 13125, 0, 0, 2, 1, 0, 0)"

            if (args.Length <= 0)
            {
                // List all mailtypes
                var s = "[TestMail] " + GetCommandHelpText() + "\r";
                s += "Possible MailTypes:\r";
                character.SendMessage(s);
                s = string.Empty;
                foreach (var t in Enum.GetValues(typeof(MailType)))
                {
                    s += string.Format("{0}={1}", (byte)t, t.ToString());
                    s += "  ";
                    if ((byte)t == 25) // Max on 1.2, we don't need to list more
                        break;
                }
                character.SendMessage(s);
                return;
            }

            MailType mType = MailType.InvalidMailType;
            if (args.Length > 0)
            {
                var a0 = args[0].ToLower();
                switch (a0)
                {
                    case "list":
                        character.SendMessage("[TestMail] List of Mails");
                        foreach (var m in MailManager.Instance.GetCurrentMailList(character))
                            character.SendMessage("{0} - {1} - {2}", m.Value.Id, m.Value.MailType, m.Value.Title);
                        character.SendMessage("[TestMail] End of List");
                        return;
                    case "clear":
                        character.SendMessage("[TestMail] Clear List of Mails");
                        foreach (var m in MailManager.Instance.GetCurrentMailList(character))
                            character.Mails.DeleteMail(m.Value.Id, false);
                        return;
                    default:
                        if (MailType.TryParse(args[0], out MailType mTypeByName))
                            mType = mTypeByName;
                        break;
                }
            }

            if (mType == MailType.InvalidMailType)
            {
                character.SendMessage("[TestMail] Invalid type: {0}", mType);
                return;
            }

            character.SendMessage("[TestMail] Testing type: {0}", mType);
            try
            {
                var mail = new BaseMail();

                mail.MailType = mType;
                mail.Title = "TestMail " + mType.ToString();

                mail.Header.SenderId = character.Id;
                mail.Header.SenderName = character.Name;
                mail.Header.ReceiverId = character.Id;
                mail.Header.ReceiverName = character.Name;
                mail.Body.ReceiverName = character.Name;

                mail.Header.Attachments = 0;
                mail.Header.Extra = 0;

                mail.Body.Text = "Test Mail Body";
                mail.Body.SendDate = DateTime.UtcNow;
                mail.Body.RecvDate = DateTime.UtcNow;

                if (args.Length > 1)
                {
                    var n = args[1];
                    mail.Header.SenderName = n;
                    mail.Header.SenderId = NameManager.Instance.GetCharacterId(n);
                    character.SendMessage("[TestMail] From: {0}", n);
                }

                if (args.Length > 2)
                {
                    var n = args[2];
                    mail.Title = n;
                    character.SendMessage("[TestMail] Title: {0}", n);
                }

                if (args.Length > 3)
                {
                    var n = args[3];
                    mail.Body.Text = n;
                    character.SendMessage("[TestMail] Body: {0}", n);
                }

                if (args.Length > 4)
                {
                    var n = args[4];
                    if (int.TryParse(n, out var copper))
                    {
                        mail.Body.CopperCoins = copper;
                        character.SendMessage("[TestMail] Money: {0}", copper);
                    }
                    else
                    {
                        character.SendMessage("[TestMail] Money parse error");
                    }
                }

                if (args.Length > 5)
                {
                    var n = args[5];
                    if (int.TryParse(n, out var extraFlag))
                    {
                        mail.Header.Extra = extraFlag;
                        character.SendMessage("[TestMail] Extra: {0}", extraFlag);
                    }
                    else
                    {
                        character.SendMessage("[TestMail] Extra Flag parse error");
                    }
                }


                mail.Send();
            }
            catch (Exception e)
            {
                character.SendMessage("[TestMail] Exception: {0}", e.Message);
            }
        }
    }
}

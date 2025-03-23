using System;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestMails : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "testmail", "test_mail" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<action||mailType> [senderName] [title] [body] [money] [billing] [[attachmentItems ID/Counts]]";
    }

    public string GetCommandHelpText()
    {
        return "Execute a mail related action or sends a dummy mail to yourself of a given type\r" +
               "Allowed actions: list, clear, check";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        // Example: Dummy pack base 1g at 125% no bonus, seller is not the crafter
        // /testmail 19 .sellBackpack Payment "body('Dummy Pack', 125, 12500, 10000, 0, 10500, 0, 1, 0, 0)"

        // Example: Dummy pack base 1g at 125% no bonus, seller is crafter
        // /testmail 19 .sellBackpack Payment "body('Dummy Pack', 125, 12500, 13125, 0, 0, 2, 1, 0, 0)"

        if (args.Length <= 0)
        {
            // List all mailTypes
            CommandManager.SendNormalText(this, messageOutput, $"{GetCommandHelpText()}\rPossible MailTypes:");
            var s = string.Empty;
            foreach (var t in Enum.GetValues(typeof(MailType)))
            {
                s += $"{(byte)t}={t}";
                s += "  ";
            }

            CommandManager.SendNormalText(this, messageOutput, s);
            return;
        }

        var mType = MailType.InvalidMailType;
        if (args.Length > 0)
        {
            var a0 = args[0].ToLower();
            switch (a0)
            {
                case "list":
                    CommandManager.SendNormalText(this, messageOutput, $"List of Mails");
                    foreach (var m in MailManager.Instance.GetCurrentMailList(character))
                    {
                        CommandManager.SendNormalText(this, messageOutput,
                            $"{m.Value.Id} - {m.Value.MailType} - ({m.Value.Header.Status}) {m.Value.Title}");
                    }

                    CommandManager.SendNormalText(this, messageOutput, $"End of List");
                    return;
                case "clear":
                    CommandManager.SendNormalText(this, messageOutput, $"Clear List of Mails");
                    foreach (var m in MailManager.Instance.GetCurrentMailList(character))
                    {
                        character.Mails.DeleteMail(m.Value.Id, false);
                    }

                    return;
                case "check":
                    character.Mails.SendUnreadMailCount();
                    character.SendMessage($"[TestMail] {character.Mails.UnreadMailCount.TotalReceived} unread mails");
                    return;
                default:
                    if (Enum.TryParse(args[0], out MailType mTypeByName))
                    {
                        mType = mTypeByName;
                    }

                    break;
            }
        }

        if (mType == MailType.InvalidMailType)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Invalid type: {mType}");
            return;
        }

        CommandManager.SendNormalText(this, messageOutput, $"Testing type: {mType}");
        try
        {
            var mail = new BaseMail();

            mail.MailType = mType;
            mail.Title = "TestMail " + mType.ToString();
            mail.ReceiverName = character.Name;

            mail.Header.SenderId = character.Id;
            mail.Header.SenderName = character.Name;
            mail.Header.ReceiverId = character.Id;

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
                CommandManager.SendNormalText(this, messageOutput, $"From: {n}");
            }

            if (args.Length > 2)
            {
                var n = args[2];
                mail.Title = n;
                CommandManager.SendNormalText(this, messageOutput, $"Title: {n}");
            }

            if (args.Length > 3)
            {
                var n = args[3];
                mail.Body.Text = n;
                CommandManager.SendNormalText(this, messageOutput, $"Body: {n}");
            }

            if (args.Length > 4)
            {
                var n = args[4];
                if (int.TryParse(n, out var copper))
                {
                    mail.Body.CopperCoins = copper;
                    CommandManager.SendNormalText(this, messageOutput, $"Money: {copper}");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Money parse error");
                }
            }

            if (args.Length > 5)
            {
                var n = args[5];
                if (int.TryParse(n, out var billing))
                {
                    mail.Body.BillingAmount = billing;
                    CommandManager.SendNormalText(this, messageOutput, $"BillingCost: {billing}");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput, $"Billing Cost parse error");
                }
            }

            if (args.Length > 6)
            {
                for (var a = 6; a < args.Length - 1; a += 2)
                {
                    var iStr = args[a];
                    var cStr = args[a + 1];
                    if (uint.TryParse(iStr, out var itemId))
                    {
                        if (int.TryParse(cStr, out var itemCount))
                        {
                            var itemTemplate = ItemManager.Instance.GetTemplate(itemId);
                            if (itemTemplate == null)
                            {
                                CommandManager.SendErrorText(this, messageOutput,
                                    $"Template does not exist for {itemId} !");
                                return;
                            }

                            if (itemCount < 1)
                            {
                                itemCount = 1;
                            }

                            if (itemCount > itemTemplate.MaxCount)
                            {
                                itemCount = itemTemplate.MaxCount;
                            }

                            var itemGrade = itemTemplate.FixedGrade;
                            if (itemGrade <= 0)
                            {
                                itemGrade = 0;
                            }

                            var newItem = ItemManager.Instance.Create(itemId, itemCount, (byte)itemGrade, true);
                            newItem.OwnerId = character.Id;
                            newItem.SlotType = SlotType.MailAttachment;
                            mail.Body.Attachments.Add(newItem);

                            CommandManager.SendNormalText(this, messageOutput,
                                $"Attachment: @ITEM_NAME({itemId}) ({itemId}) x {itemCount}");
                        }
                        else
                        {
                            CommandManager.SendErrorText(this, messageOutput, $"Parse Error on ItemCount");
                        }
                    }
                    else
                    {
                        CommandManager.SendErrorText(this, messageOutput, $"Parse Error on ItemID");
                    }
                }
            }

            mail.Send();
        }
        catch (Exception e)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Exception: {e.Message}");
        }
    }
}

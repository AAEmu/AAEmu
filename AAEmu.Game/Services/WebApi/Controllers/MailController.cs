using System;
using System.Collections.Generic;
using System.Text.Json;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Mails.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Services.WebApi.Models;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class MailController : BaseController
{
    [WebApiPost("/mail/send")]
    public HttpResponse Send(HttpRequest request)
    {
        var mailRequest = JsonSerializer.Deserialize<SendMailRequest>(request.Body);

        if (mailRequest == null)
        {
            return BadRequestJson(new ErrorModel("Invalid mail request"));
        }

        if (string.IsNullOrWhiteSpace(mailRequest.SenderName))
        {
            mailRequest.SenderName = "GM";
        }

        // Check if the title and body are empty
        if (string.IsNullOrWhiteSpace(mailRequest.Title))
        {
            return BadRequestJson(new ErrorModel("Email Title cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(mailRequest.Body))
        {
            return BadRequestJson(new ErrorModel("Email body cannot be empty."));
        }

        // Check if Money and Billing are less than 0
        if (mailRequest.Money < 0)
        {
            return BadRequestJson(new ErrorModel("Money cannot be less than 0."));
        }

        if (mailRequest.Billing < 0)
        {
            return BadRequestJson(new ErrorModel("Billing cannot be less than 0."));
        }

        // Check if Receiver Type is a valid value in the Recipient Type enumeration
        if (!Enum.IsDefined(typeof(RecipientType), mailRequest.RecipientType))
        {
            return BadRequestJson(new ErrorModel("Invalid recipient type."));
        }

        List<Character> characters = [];
        switch (mailRequest.RecipientType)
        {
            case RecipientType.Expedition:
                {
                    foreach (uint recipient in mailRequest.Recipients)
                    {
                        var expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)recipient);
                        if (expedition == null || expedition.IsDisbanded)
                        {
                            return BadRequestJson(new ErrorModel($"Invalid ExpeditionId: {recipient} ."));
                        }

                        foreach (var expeditionMember in expedition.Members)
                        {
                            var character = WorldManager.Instance.GetCharacterById(expeditionMember.CharacterId) ??
                                            Character.Load(expeditionMember.CharacterId);
                            if (character == null || character.DeleteTime > DateTime.MinValue ||
                                character.DeleteRequestTime > DateTime.MinValue)
                            {
                                continue;
                            }

                            characters.Add(character);
                        }
                    }
                }
                break;
            case RecipientType.Family:
                {
                    foreach (uint recipient in mailRequest.Recipients)
                    {
                        var family = FamilyManager.Instance.GetFamily(recipient);
                        if (family == null)
                        {
                            return BadRequestJson(new ErrorModel($"Invalid FamilyId: {recipient} ."));
                        }

                        foreach (var member in family.Members)
                        {
                            var character = member.Character;
                            if (character == null || character.DeleteTime > DateTime.MinValue ||
                                character.DeleteRequestTime > DateTime.MinValue)
                            {
                                continue;
                            }

                            characters.Add(character);
                        }
                    }
                }
                break;
            case RecipientType.Online:
                characters.AddRange(WorldManager.Instance.GetAllCharacters());
                break;
            case RecipientType.All:
                {
                    using (var connection = MySQL.CreateConnection())
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText =
                                "SELECT `id`, `name`, `account_id` FROM `characters` where `deleted` = 0";
                            command.Prepare();
                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    var character = new Character(new UnitCustomModelParams());
                                    character.Id = reader.GetUInt32("id");
                                    character.AccountId = reader.GetUInt32("account_id");
                                    character.Name = reader.GetString("name");

                                    characters.Add(character);
                                }
                            }
                        }
                    }
                }
                break;
            case RecipientType.Character:
            default:
                {
                    foreach (uint recipient in mailRequest.Recipients)
                    {
                        var character = WorldManager.Instance.GetCharacterById(recipient) ??
                                        Character.Load(recipient);
                        if (character == null || character.DeleteTime > DateTime.MinValue ||
                            character.DeleteRequestTime > DateTime.MinValue)
                        {
                            return BadRequestJson(new ErrorModel($"Invalid characterId: {recipient} ."));
                        }

                        characters.Add(character);
                    }
                }
                break;
        }

        foreach (var attachmentItem in mailRequest.AttachmentItems)
        {
            var itemTemplate = ItemManager.Instance.GetTemplate(attachmentItem.Id);
            if (itemTemplate == null)
            {
                return BadRequestJson(new ErrorModel($"Template does not exist for {attachmentItem.Id} !"));
            }
        }

        if (characters.Count < 1)
        {
            return BadRequestJson(new ErrorModel("Recipients is empty."));
        }

        var sended = 0;
        foreach (var character in characters)
        {
            var mail = new BaseMail();
            mail.MailType = mailRequest.Type;
            mail.Title = mailRequest.Title;
            mail.ReceiverName = character.Name;

            mail.Header.SenderId = (uint)SystemMailSenderKind.None;
            mail.Header.SenderName = mailRequest.SenderName;
            mail.Header.ReceiverId = character.Id;

            mail.Header.Extra = 0;

            mail.Body.Text = mailRequest.Body;
            mail.Body.SendDate = DateTime.UtcNow;
            mail.Body.RecvDate = DateTime.UtcNow;
            mail.Body.CopperCoins = mailRequest.Money;
            mail.Body.BillingAmount = mailRequest.Billing;

            foreach (var attachmentItem in mailRequest.AttachmentItems)
            {
                var itemTemplate = ItemManager.Instance.GetTemplate(attachmentItem.Id);

                if (attachmentItem.Count < 1)
                {
                    attachmentItem.Count = 1;
                }

                if (attachmentItem.Count > itemTemplate.MaxCount)
                {
                    attachmentItem.Count = itemTemplate.MaxCount;
                }

                var itemGrade = itemTemplate.FixedGrade;
                if (itemGrade <= 0)
                {
                    itemGrade = 0;
                }

                var newItem = ItemManager.Instance.Create(itemTemplate.Id, attachmentItem.Count, (byte)itemGrade);
                newItem.OwnerId = character.Id;
                newItem.SlotType = SlotType.MailAttachment;
                mail.Body.Attachments.Add(newItem);
            }

            if (mail.Send())
            {
                sended++;
            }
        }

        return OkJson(new { SendCount = sended });
    }
}

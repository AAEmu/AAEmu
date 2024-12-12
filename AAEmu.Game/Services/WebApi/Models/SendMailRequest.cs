using System.Collections.Generic;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Services.WebApi.Models;

public class SendMailRequest
{
    public MailType Type { get; set; } = MailType.SysExpress;
    public string SenderName { get; set; } = "GM"; // Default value
    public List<uint> Recipients { get; set; } = []; // Recipient Id 
    public RecipientType RecipientType { get; set; } = 0; // Game characters, game guilds, default game characters
    public string Title { get; set; }
    public string Body { get; set; }
    public int Money { get; set; } = 0; // Default 0
    public int Billing { get; set; } = 0; // Default 0
    public List<AttachmentItem> AttachmentItems { get; set; } = []; // Default empty
}

public enum RecipientType
{
    Character = 0,
    Expedition = 1,
    Family = 2,
    Online = 3,// Online characters
    All = 4, // All characters, Excluding deleted characters
}

public class AttachmentItem
{
    public uint Id { get; set; } // Item id
    public int Count { get; set; } = 1; // Item count, default 1
}

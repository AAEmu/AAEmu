using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// In-game mails
/// </summary>
public partial class Mail
{
    public int Id { get; set; }

    public int Type { get; set; }

    public int Status { get; set; }

    public string Title { get; set; }

    public string Text { get; set; }

    public int SenderId { get; set; }

    public string SenderName { get; set; }

    public int AttachmentCount { get; set; }

    public int ReceiverId { get; set; }

    public string ReceiverName { get; set; }

    public DateTime OpenDate { get; set; }

    public DateTime SendDate { get; set; }

    public DateTime ReceivedDate { get; set; }

    public int Returned { get; set; }

    public long Extra { get; set; }

    public int MoneyAmount1 { get; set; }

    public int MoneyAmount2 { get; set; }

    public int MoneyAmount3 { get; set; }

    public long Attachment0 { get; set; }

    public long Attachment1 { get; set; }

    public long Attachment2 { get; set; }

    public long Attachment3 { get; set; }

    public long Attachment4 { get; set; }

    public long Attachment5 { get; set; }

    public long Attachment6 { get; set; }

    public long Attachment7 { get; set; }

    public long Attachment8 { get; set; }

    public long Attachment9 { get; set; }
}

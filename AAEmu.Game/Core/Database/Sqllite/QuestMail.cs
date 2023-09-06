using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestMail
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Comment { get; set; }

    public long? CategoryId { get; set; }

    public string Text { get; set; }

    public long? NpcId { get; set; }

    public string SenderName { get; set; }

    public long? SendMoney { get; set; }

    public long? QuestMailAttachmentId { get; set; }
}

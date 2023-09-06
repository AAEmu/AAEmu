using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestMailAttachmentItem
{
    public long? Id { get; set; }

    public long? QuestMailAttachmentId { get; set; }

    public long? ItemId { get; set; }

    public long? Count { get; set; }

    public long? GradeId { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestContextText
{
    public long? Id { get; set; }

    public long? QuestContextTextKindId { get; set; }

    public long? QuestContextId { get; set; }

    public string Text { get; set; }
}

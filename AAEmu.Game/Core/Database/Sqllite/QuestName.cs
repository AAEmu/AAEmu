using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestName
{
    public long? Id { get; set; }

    public long? QuestNameKindId { get; set; }

    public long? QuestContextId { get; set; }

    public string Name { get; set; }
}

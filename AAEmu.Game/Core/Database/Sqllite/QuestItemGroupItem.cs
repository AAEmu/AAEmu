using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestItemGroupItem
{
    public long? Id { get; set; }

    public long? QuestItemGroupId { get; set; }

    public long? ItemId { get; set; }
}

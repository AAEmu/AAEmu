using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestMonsterNpc
{
    public long? Id { get; set; }

    public long? QuestMonsterGroupId { get; set; }

    public long? NpcId { get; set; }
}

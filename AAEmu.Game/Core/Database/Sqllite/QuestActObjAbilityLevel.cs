using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjAbilityLevel
{
    public long? Id { get; set; }

    public long? AbilityId { get; set; }

    public long? Level { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

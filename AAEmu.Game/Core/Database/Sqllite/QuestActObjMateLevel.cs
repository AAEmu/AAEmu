using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjMateLevel
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? Level { get; set; }

    public byte[] Cleanup { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

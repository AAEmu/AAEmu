using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjDoodadPhaseCheck
{
    public long? Id { get; set; }

    public long? DoodadId { get; set; }

    public long? Phase1 { get; set; }

    public long? Phase2 { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

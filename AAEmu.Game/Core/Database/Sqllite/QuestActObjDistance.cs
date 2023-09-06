using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjDistance
{
    public long? Id { get; set; }

    public byte[] Within { get; set; }

    public long? NpcId { get; set; }

    public long? Distance { get; set; }

    public long? HighlightDoodadId { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

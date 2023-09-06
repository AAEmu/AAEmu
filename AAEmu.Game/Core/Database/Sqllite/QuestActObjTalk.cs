using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjTalk
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public byte[] TeamShare { get; set; }

    public long? ItemId { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }

    public long? HighlightDoodadPhase { get; set; }

    public long? HighlightDoodadId { get; set; }
}

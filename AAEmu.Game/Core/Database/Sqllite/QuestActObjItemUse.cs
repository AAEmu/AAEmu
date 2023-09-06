using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjItemUse
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? Count { get; set; }

    public string Cinema { get; set; }

    public long? HighlightDoodadId { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }

    public byte[] DropWhenDestroy { get; set; }

    public long? HighlightDoodadPhase { get; set; }
}

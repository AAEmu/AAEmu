using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjCraft
{
    public long? Id { get; set; }

    public long? CraftId { get; set; }

    public long? Count { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }

    public long? HighlightDoodadId { get; set; }

    public long? HighlightDoodadPhase { get; set; }
}

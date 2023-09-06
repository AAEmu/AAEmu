using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjInteraction
{
    public long? Id { get; set; }

    public long? WiId { get; set; }

    public long? Count { get; set; }

    public long? DoodadId { get; set; }

    public byte[] UseAlias { get; set; }

    public byte[] TeamShare { get; set; }

    public long? HighlightDoodadId { get; set; }

    public long? QuestActObjAliasId { get; set; }

    public long? Phase { get; set; }

    public long? HighlightDoodadPhase { get; set; }
}

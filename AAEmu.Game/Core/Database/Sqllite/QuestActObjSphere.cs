using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjSphere
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? SphereId { get; set; }

    public long? NpcId { get; set; }

    public string Cinema { get; set; }

    public long? HighlightDoodadId { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }

    public long? HighlightDoodadPhase { get; set; }
}

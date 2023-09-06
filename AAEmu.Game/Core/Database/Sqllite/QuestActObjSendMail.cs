using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjSendMail
{
    public long? Id { get; set; }

    public long? Item1Id { get; set; }

    public long? Count1 { get; set; }

    public long? Item2Id { get; set; }

    public long? Count2 { get; set; }

    public long? Item3Id { get; set; }

    public long? Count3 { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

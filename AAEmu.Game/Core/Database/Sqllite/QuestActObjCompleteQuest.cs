using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjCompleteQuest
{
    public long? Id { get; set; }

    public long? QuestId { get; set; }

    public byte[] AcceptWith { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

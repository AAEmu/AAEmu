using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjAggro
{
    public long? Id { get; set; }

    public long? Range { get; set; }

    public long? Rank1 { get; set; }

    public long? Rank2 { get; set; }

    public long? Rank3 { get; set; }

    public long? Rank1Ratio { get; set; }

    public long? Rank2Ratio { get; set; }

    public long? Rank3Ratio { get; set; }

    public byte[] Rank1Item { get; set; }

    public byte[] Rank2Item { get; set; }

    public byte[] Rank3Item { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }
}

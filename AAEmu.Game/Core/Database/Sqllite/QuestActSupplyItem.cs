using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActSupplyItem
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? Count { get; set; }

    public long? GradeId { get; set; }

    public byte[] ShowActionBar { get; set; }

    public byte[] Cleanup { get; set; }

    public byte[] DropWhenDestroy { get; set; }

    public byte[] DestroyWhenDrop { get; set; }
}

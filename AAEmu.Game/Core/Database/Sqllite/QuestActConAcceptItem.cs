using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActConAcceptItem
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public byte[] Cleanup { get; set; }

    public byte[] DropWhenDestroy { get; set; }

    public byte[] DestroyWhenDrop { get; set; }
}

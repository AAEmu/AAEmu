using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActCheckDistance
{
    public long? Id { get; set; }

    public byte[] Within { get; set; }

    public long? NpcId { get; set; }

    public long? Distance { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActConFail
{
    public long? Id { get; set; }

    public byte[] ForceChangeComponent { get; set; }
}

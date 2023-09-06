using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BattleFieldBuff
{
    public long? Id { get; set; }

    public long? BattleFieldId { get; set; }

    public long? BuffId { get; set; }

    public long? Value { get; set; }
}

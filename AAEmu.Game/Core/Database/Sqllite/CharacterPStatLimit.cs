using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CharacterPStatLimit
{
    public long? Id { get; set; }

    public long? PStatId { get; set; }

    public long? Min { get; set; }

    public long? Max { get; set; }
}

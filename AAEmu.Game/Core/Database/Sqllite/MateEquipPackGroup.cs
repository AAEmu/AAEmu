﻿using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MateEquipPackGroup
{
    public long? Id { get; set; }

    public long? NpcId { get; set; }

    public long? MateEquipPackId { get; set; }
}

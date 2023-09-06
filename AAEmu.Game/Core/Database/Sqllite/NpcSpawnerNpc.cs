using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcSpawnerNpc
{
    public long? Id { get; set; }

    public long? NpcSpawnerId { get; set; }

    public long? MemberId { get; set; }

    public string MemberType { get; set; }

    public double? Weight { get; set; }
}
